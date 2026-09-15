#!/usr/bin/env python3
"""Behavioral regression cases for the diagram-design Slogs overlay."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MODULE_PATH = ROOT / "scripts" / "verify-connectors.py"
SPEC = importlib.util.spec_from_file_location("verify_connectors", MODULE_PATH)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


def svg(nodes: str, edges: str) -> str:
    return f'<svg role="img"><title>T</title><desc>D</desc>{edges}{nodes}</svg>'


GOOD_CENTERED = svg(
    '<rect data-node-id="top" x="100" y="40" width="160" height="80"/>'
    '<rect data-node-id="bottom" x="100" y="168" width="160" height="80"/>',
    '<path marker-end="url(#a)" data-edge-from="bottom" data-edge-to="top" d="M180 168V120"/>',
)

SHORT_TERMINAL_STUB = svg(
    '<rect data-node-id="source" x="20" y="40" width="120" height="80"/>'
    '<rect data-node-id="target" x="300" y="200" width="160" height="80"/>',
    '<path marker-end="url(#a)" data-edge-from="source" data-edge-to="target" d="M140 80H476V240H460"/>',
)

CRAMPED_OFF_CENTER = svg(
    '<rect data-node-id="top" x="100" y="40" width="160" height="80"/>'
    '<rect data-node-id="bottom" x="100" y="140" width="160" height="80"/>',
    '<path marker-end="url(#a)" data-edge-from="bottom" data-edge-to="top" d="M120 140H72V120H120"/>',
)

NESTED_MODULE = svg(
    '<rect data-node-id="adapter" x="20" y="80" width="120" height="96"/>'
    '<rect data-node-id="module" x="188" y="40" width="320" height="176"/>'
    '<rect x="212" y="104" width="80" height="72"/>'
    '<rect x="308" y="104" width="80" height="72"/>'
    '<rect x="404" y="104" width="80" height="72"/>',
    '<path marker-end="url(#a)" data-edge-from="adapter" data-edge-to="module" d="M140 128H188"/>',
)

assert MODULE.verify(GOOD_CENTERED) == [], MODULE.verify(GOOD_CENTERED)
assert MODULE.verify(NESTED_MODULE) == [], MODULE.verify(NESTED_MODULE)

short = MODULE.verify(SHORT_TERMINAL_STUB)
assert any("terminal shaft 16px" in message for message in short), short

cramped = MODULE.verify(CRAMPED_OFF_CENTER)
assert any("vertical gap" in message for message in cramped), cramped
assert any("not centered" in message for message in cramped), cramped
assert any("direct centerline" in message for message in cramped), cramped

print("PASS: 4 overlay cases; short terminal shaft and cramped detour rejected")
