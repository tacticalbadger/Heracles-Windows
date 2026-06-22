import { Extension } from '@tiptap/core';
import { Plugin } from 'prosemirror-state';
import type { EditorView } from 'prosemirror-view';
import type { Node as PMNode } from 'prosemirror-model';

export const BLOCK_DRAG_MIME = 'application/x-heracles-block';

// Fraction of a block's width (measured from the right) that triggers
// "make a column" instead of a plain reorder.
const COLUMN_EDGE_FRACTION = 0.7;

export type OuterBlock = { pos: number; node: PMNode; parentIsColumn: boolean };

export type DropTarget = OuterBlock & {
  mode: 'column' | 'reorder';
  side: 'right' | 'before' | 'after';
  rect: DOMRect;
};

/**
 * Resolve a document position to the block that sits directly in the main flow
 * (parent is `doc`) or directly inside a `column`. This is the unit that gets
 * moved/columnised — never a `column` or `columnBlock` itself.
 */
export const resolveOuterBlock = (doc: PMNode, pos: number): OuterBlock | null => {
  const $pos = doc.resolve(Math.max(0, Math.min(pos, doc.content.size)));
  for (let depth = $pos.depth; depth >= 1; depth -= 1) {
    const parent = $pos.node(depth - 1);
    const parentName = parent.type.name;
    if (parentName === 'doc' || parentName === 'column') {
      const node = $pos.node(depth);
      if (node.type.name === 'columnBlock' || node.type.name === 'column') {
        return null;
      }
      return { pos: $pos.before(depth), node, parentIsColumn: parentName === 'column' };
    }
  }
  return null;
};

/** Figure out the drop target + whether the cursor is in a column edge zone. */
export const getDropTarget = (view: EditorView, clientX: number, clientY: number): DropTarget | null => {
  const coords = view.posAtCoords({ left: clientX, top: clientY });
  if (!coords) return null;
  const block = resolveOuterBlock(view.state.doc, coords.pos);
  if (!block) return null;

  const dom = view.nodeDOM(block.pos);
  const el = dom instanceof HTMLElement ? dom : dom?.parentElement ?? null;
  if (!el) return null;
  const rect = el.getBoundingClientRect();

  const relX = (clientX - rect.left) / Math.max(1, rect.width);
  if (relX > COLUMN_EDGE_FRACTION) {
    return { ...block, mode: 'column', side: 'right', rect };
  }
  const before = clientY < rect.top + rect.height / 2;
  return { ...block, mode: 'reorder', side: before ? 'before' : 'after', rect };
};

/**
 * Apply a block drop: build and dispatch one transaction. Returns true when it
 * did something. The ColumnsNormalizer cleans up any emptied/under-filled
 * columns afterwards.
 */
export const applyBlockDrop = (view: EditorView, sourcePos: number, target: DropTarget): boolean => {
  const { state } = view;
  const { doc, schema } = state;
  const sourceNode = doc.nodeAt(sourcePos);
  if (!sourceNode) return false;

  // Never drop a block onto itself or into its own subtree.
  if (target.pos >= sourcePos && target.pos < sourcePos + sourceNode.nodeSize) return false;

  const targetNode = doc.nodeAt(target.pos);
  if (!targetNode) return false;

  const columnType = schema.nodes.column;
  const columnBlockType = schema.nodes.columnBlock;
  if (!columnType || !columnBlockType) return false;

  let tr = state.tr;

  if (target.mode === 'column') {
    if (target.parentIsColumn) {
      // Insert a new column to the right of the target's column.
      const $col = doc.resolve(target.pos);
      const columnEnd = $col.after($col.depth);
      const newColumn = columnType.create({ width: 1 }, sourceNode);
      tr.insert(columnEnd, newColumn);
    } else {
      // Wrap the target block + source block into a new 2-column layout.
      const colA = columnType.create({ width: 1 }, targetNode);
      const colB = columnType.create({ width: 1 }, sourceNode);
      const columnBlock = columnBlockType.create(null, [colA, colB]);
      tr.replaceRangeWith(target.pos, target.pos + targetNode.nodeSize, columnBlock);
    }
  } else {
    // Plain reorder: drop above/below the target (works in the flow or in a column).
    const insertPos = target.side === 'before' ? target.pos : target.pos + targetNode.nodeSize;
    if (insertPos === sourcePos || insertPos === sourcePos + sourceNode.nodeSize) {
      return false;
    }
    tr.insert(insertPos, sourceNode);
  }

  // Remove the source from its original location (map through the insert above).
  const mappedSource = tr.mapping.map(sourcePos);
  tr.delete(mappedSource, mappedSource + sourceNode.nodeSize);

  view.dispatch(tr.scrollIntoView());
  return true;
};

/**
 * Keeps column structure sane after edits: drops empty columns and unwraps any
 * `columnBlock` left with fewer than two columns back into the normal flow.
 * Fixes one issue per pass (re-runs until stable), mirroring the appendTransaction
 * pattern used by BlockId.
 */
export const ColumnsNormalizer = Extension.create({
  name: 'columnsNormalizer',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        appendTransaction: (transactions, _oldState, newState) => {
          if (!transactions.some((transaction) => transaction.docChanged)) {
            return null;
          }

          const tr = newState.tr;
          let changed = false;

          newState.doc.descendants((node, pos) => {
            if (changed) return false;
            if (node.type.name !== 'columnBlock') return undefined;

            // 1) Remove the first empty column found.
            let emptyPos = -1;
            node.forEach((child, offset) => {
              if (emptyPos < 0 && child.type.name === 'column' && child.childCount === 0) {
                emptyPos = pos + 1 + offset;
              }
            });
            if (emptyPos >= 0) {
              const col = newState.doc.nodeAt(emptyPos);
              if (col) {
                tr.delete(emptyPos, emptyPos + col.nodeSize);
                changed = true;
              }
              return false;
            }

            // 2) Unwrap a columnBlock that no longer has 2+ columns.
            if (node.childCount < 2) {
              if (node.childCount === 0) {
                tr.delete(pos, pos + node.nodeSize);
              } else {
                tr.replaceWith(pos, pos + node.nodeSize, node.child(0).content);
              }
              changed = true;
              return false;
            }
            return undefined;
          });

          return changed ? tr : null;
        }
      })
    ];
  }
});

export default ColumnsNormalizer;
