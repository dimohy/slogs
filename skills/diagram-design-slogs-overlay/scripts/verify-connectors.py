#!/usr/bin/env python3
"""Verify diagram connector ports, gaps, and visible arrow shafts."""

from __future__ import annotations

import argparse
import math
import re
import sys
from collections import defaultdict
from dataclasses import dataclass
from html.parser import HTMLParser
from pathlib import Path

EPSILON = 0.05
MIN_NODE_GAP = 32.0
MIN_SHAFT = 32.0
MIN_SHARED_PORT_GAP = 12.0
TOKEN_RE = re.compile(r"[A-Za-z]|-?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?")
ARITY = {"M": 2, "L": 2, "H": 1, "V": 1, "C": 6, "S": 4, "Q": 4, "T": 2, "A": 7, "Z": 0}


@dataclass(frozen=True)
class Rect:
    node_id: str
    x: float
    y: float
    width: float
    height: float

    @property
    def cx(self) -> float: return self.x + self.width / 2

    @property
    def cy(self) -> float: return self.y + self.height / 2

    @property
    def right(self) -> float: return self.x + self.width

    @property
    def bottom(self) -> float: return self.y + self.height


@dataclass(frozen=True)
class Edge:
    source: str
    target: str
    start: tuple[float, float]
    end: tuple[float, float]
    commands: tuple[str, ...]
    geometry: str
    first_shaft: float
    terminal_shaft: float


def path_geometry(data: str) -> tuple[tuple[float, float], tuple[float, float], tuple[str, ...], float, float]:
    tokens = TOKEN_RE.findall(data)
    if not tokens or tokens[0] != "M":
        raise ValueError("path must start with an absolute M command")
    index, x, y = 0, 0.0, 0.0
    start: tuple[float, float] | None = None
    commands: list[str] = []
    shafts: list[float] = []
    while index < len(tokens):
        command = tokens[index]
        index += 1
        if command not in ARITY or command.islower():
            raise ValueError(f"unsupported path command {command!r}; use absolute SVG commands")
        commands.append(command)
        arity = ARITY[command]
        if index + arity > len(tokens):
            raise ValueError(f"incomplete {command} command")
        values = [float(value) for value in tokens[index:index + arity]]
        index += arity
        previous = (x, y)
        if command in {"M", "L", "T"}: x, y = values[-2:]
        elif command == "H": x = values[0]
        elif command == "V": y = values[0]
        elif command in {"C", "S", "Q", "A"}: x, y = values[-2:]
        elif command == "Z":
            if start is None: raise ValueError("Z before M")
            x, y = start
        if start is None:
            start = (x, y)
        elif command != "M":
            shafts.append(math.dist(previous, (x, y)))
    assert start is not None
    if not shafts:
        raise ValueError("path has no drawable segment")
    return start, (x, y), tuple(commands), shafts[0], shafts[-1]


def boundary_side(point: tuple[float, float], node: Rect) -> str | None:
    x, y = point
    within_x = node.x - EPSILON <= x <= node.right + EPSILON
    within_y = node.y - EPSILON <= y <= node.bottom + EPSILON
    if within_y and abs(x - node.x) <= EPSILON: return "left"
    if within_y and abs(x - node.right) <= EPSILON: return "right"
    if within_x and abs(y - node.y) <= EPSILON: return "top"
    if within_x and abs(y - node.bottom) <= EPSILON: return "bottom"
    return None


def port_offset(point: tuple[float, float], side: str) -> float:
    return point[1] if side in {"left", "right"} else point[0]


class Parser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.nodes: dict[str, Rect] = {}
        self.edges: list[Edge] = []
        self.unannotated_directed = 0

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        data = {key.casefold(): value or "" for key, value in attrs}
        if tag == "rect" and data.get("data-node-id"):
            node_id = data["data-node-id"]
            if node_id in self.nodes: raise ValueError(f"duplicate data-node-id {node_id!r}")
            self.nodes[node_id] = Rect(node_id, *[float(data[key]) for key in ("x", "y", "width", "height")])
        if tag not in {"path", "line"} or "marker-end" not in data:
            return
        source, target = data.get("data-edge-from"), data.get("data-edge-to")
        if not source or not target:
            self.unannotated_directed += 1
            return
        if tag == "path":
            start, end, commands, first_shaft, terminal_shaft = path_geometry(data.get("d", ""))
            geometry = data.get("d", "")
        else:
            start = (float(data["x1"]), float(data["y1"]))
            end = (float(data["x2"]), float(data["y2"]))
            commands = ("M", "L")
            geometry = f"M{start[0]},{start[1]}L{end[0]},{end[1]}"
            first_shaft = terminal_shaft = math.dist(start, end)
        self.edges.append(Edge(source, target, start, end, commands, geometry, first_shaft, terminal_shaft))


def verify(source: str) -> list[str]:
    errors: list[str] = []
    parser = Parser()
    try: parser.feed(source); parser.close()
    except (ValueError, KeyError) as exc: return [str(exc)]
    if parser.unannotated_directed:
        errors.append(f"{parser.unannotated_directed} directed connector(s) lack data-edge-from/data-edge-to")
    if not parser.edges:
        return errors + ["no annotated directed connectors found"]
    ports: dict[tuple[str, str], list[float]] = defaultdict(list)
    seen_geometry: set[str] = set()
    seen_pairs: set[tuple[str, str]] = set()
    edge_sides: list[tuple[Edge, str, str]] = []
    for edge in parser.edges:
        pair = (edge.source, edge.target)
        if pair in seen_pairs: errors.append(f"duplicate edge {edge.source}->{edge.target}")
        seen_pairs.add(pair)
        if edge.geometry in seen_geometry: errors.append(f"shared connector geometry {edge.source}->{edge.target}")
        seen_geometry.add(edge.geometry)
        source_node, target_node = parser.nodes.get(edge.source), parser.nodes.get(edge.target)
        if source_node is None or target_node is None:
            errors.append(f"missing endpoint node for {edge.source}->{edge.target}")
            continue
        source_side, target_side = boundary_side(edge.start, source_node), boundary_side(edge.end, target_node)
        if source_side is None: errors.append(f"{edge.source}->{edge.target} start is not on source boundary")
        if target_side is None: errors.append(f"{edge.source}->{edge.target} arrowhead is not on target boundary")
        if edge.first_shaft < MIN_SHAFT: errors.append(f"{edge.source}->{edge.target} first shaft {edge.first_shaft:g}px is below {MIN_SHAFT:g}px")
        if edge.terminal_shaft < MIN_SHAFT: errors.append(f"{edge.source}->{edge.target} terminal shaft {edge.terminal_shaft:g}px is below {MIN_SHAFT:g}px")
        if source_side is None or target_side is None: continue
        ports[(edge.source, source_side)].append(port_offset(edge.start, source_side))
        ports[(edge.target, target_side)].append(port_offset(edge.end, target_side))
        edge_sides.append((edge, source_side, target_side))
        if abs(source_node.cy - target_node.cy) <= EPSILON:
            gap = max(target_node.x - source_node.right, source_node.x - target_node.right)
            if gap < MIN_NODE_GAP: errors.append(f"{edge.source}->{edge.target} horizontal gap {gap:g}px is below {MIN_NODE_GAP:g}px")
            if any(command not in {"M", "H"} for command in edge.commands): errors.append(f"{edge.source}->{edge.target} aligned horizontal peers must use a direct centerline")
        if abs(source_node.cx - target_node.cx) <= EPSILON:
            gap = max(target_node.y - source_node.bottom, source_node.y - target_node.bottom)
            if gap < MIN_NODE_GAP: errors.append(f"{edge.source}->{edge.target} vertical gap {gap:g}px is below {MIN_NODE_GAP:g}px")
            if any(command not in {"M", "V"} for command in edge.commands): errors.append(f"{edge.source}->{edge.target} aligned vertical peers must use a direct centerline")
    for edge, source_side, target_side in edge_sides:
        for node_id, side, point in ((edge.source, source_side, edge.start), (edge.target, target_side, edge.end)):
            values = ports[(node_id, side)]
            node = parser.nodes[node_id]
            if len(values) == 1:
                expected = node.cy if side in {"left", "right"} else node.cx
                if abs(port_offset(point, side) - expected) > EPSILON:
                    errors.append(f"{edge.source}->{edge.target} single {node_id}.{side} port is not centered")
    for (node_id, side), values in ports.items():
        ordered = sorted(values)
        for first, second in zip(ordered, ordered[1:]):
            if second - first < MIN_SHARED_PORT_GAP:
                errors.append(f"{node_id}.{side} shared ports are only {second-first:g}px apart")
    return errors


def main() -> int:
    arguments = argparse.ArgumentParser()
    arguments.add_argument("html", type=Path)
    args = arguments.parse_args()
    errors = verify(args.html.read_text(encoding="utf-8"))
    if errors:
        print(f"FAIL {args.html}")
        for error in errors: print(f"  - {error}")
        return 1
    print(f"PASS {args.html}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

