---
name: diagram-design-slogs-overlay
description: Apply Slogs-validated connector routing and nested-module readability corrections after resolving the external diagram-design skill.
license: MIT
metadata:
  version: "1.0.0"
  overlays: "diagram-design"
---

# Diagram Design Slogs Overlay

Apply this overlay **after** the latest compatible `diagram-design` instructions have been resolved from their canonical external URL. This package is a correction layer; it must never replace, copy, pin, or shadow the external skill.

## Placement before routing

1. Place nodes before drawing connectors.
2. Keep at least 32 px between connected boxes; prefer 40–48 px on the primary flow.
3. If moving or resizing a node can make a connector direct, change the placement instead of adding an outer detour.
4. A connector that leaves the content area and returns to a nearby box is a layout failure unless the relationship truly crosses a documented boundary.

## Stable ports and arrow shafts

1. A single connector on one box edge uses that edge's geometric center.
2. Multiple connectors on one edge use distinct, evenly distributed ports at least 12 px apart.
3. Connect aligned boxes with a direct horizontal or vertical centerline.
4. The final straight segment between the last bend and the target arrowhead must be at least 32 px. A marker must not consume most of the visible shaft.
5. The first straight segment after the source should also be at least 32 px before a bend.
6. Never accept a short terminal stub merely because its endpoint touches the target boundary.

## Composite implementation modules

Represent a module's real internal structure as nested boxes, not as a prose list.

- Reserve a header row for the parent module name.
- Put child components in a separate content row or grid below the header.
- Give sibling child boxes equal visual weight unless their responsibilities differ materially.
- Do not squeeze a parent title and several tiny child boxes into one horizontal row.
- Child labels must remain readable at the requested output size; increase the parent box before reducing child boxes.
- Decorative child boxes are not connector endpoints unless they participate in the shown relationship.

## Machine verification

Annotate connected top-level rectangles with `data-node-id`. Annotate directed paths or lines with `data-edge-from` and `data-edge-to`. Then run:

```bash
python scripts/verify-connectors.py path/to/diagram.html
```

The verifier rejects missing boundary endpoints, duplicate geometry, off-center single ports, undersized box gaps, avoidable non-direct paths between aligned peers, shared ports, and source/terminal shaft segments shorter than 32 px.

## Final visual gate

After the verifier passes, render at the requested size and inspect every arrowhead at 100% scale. Machine geometry is necessary but does not replace the rendered review.

