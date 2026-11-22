import Foundation
import Testing

@testable import HeraclesApp

/// Reading a stored document into blocks, and writing blocks back.
struct EditorBlockTests {
    private let everything = """
        {"type":"doc","content":[
          {"type":"heading","attrs":{"blockId":"h1","level":2},"content":[{"type":"text","text":"Trip plan"}]},
          {"type":"paragraph","attrs":{"blockId":"p1"},"content":[
            {"type":"text","text":"Pack the "},{"type":"text","text":"lantern","marks":[{"type":"bold"}]}]},
          {"type":"bulletList","content":[
            {"type":"listItem","attrs":{"blockId":"b1"},"content":[
              {"type":"paragraph","attrs":{"blockId":"b1p"},"content":[{"type":"text","text":"Tent"}]},
              {"type":"bulletList","content":[
                {"type":"listItem","attrs":{"blockId":"b2"},"content":[
                  {"type":"paragraph","attrs":{"blockId":"b2p"},"content":[{"type":"text","text":"Pegs"}]}]}]},
              {"type":"paragraph","attrs":{"blockId":"b1q"},"content":[{"type":"text","text":"More about the tent"}]}]},
            {"type":"listItem","attrs":{"blockId":"b3"},"content":[
              {"type":"paragraph","attrs":{"blockId":"b3p"},"content":[{"type":"text","text":"Stove"}]}]}]},
          {"type":"orderedList","attrs":{"start":3},"content":[
            {"type":"listItem","attrs":{"blockId":"n1"},"content":[
              {"type":"paragraph","attrs":{"blockId":"n1p"},"content":[{"type":"text","text":"Third"}]}]}]},
          {"type":"taskList","content":[
            {"type":"taskItem","attrs":{"blockId":"t1","checked":true},"content":[
              {"type":"paragraph","attrs":{"blockId":"t1p"},"content":[{"type":"text","text":"Book ferry"}]}]},
            {"type":"taskItem","attrs":{"blockId":"t2","checked":false},"content":[
              {"type":"paragraph","attrs":{"blockId":"t2p"}}]}]},
          {"type":"blockquote","attrs":{"blockId":"q1"},"content":[
            {"type":"paragraph","attrs":{"blockId":"q1p"},"content":[{"type":"text","text":"Quoted"}]}]},
          {"type":"codeMirrorBlock","attrs":{"blockId":"c1","language":"rust","code":"fn main() {}"}},
          {"type":"calloutBlock","attrs":{"blockId":"k1","emoji":"🔥"},"content":[
            {"type":"paragraph","attrs":{"blockId":"k1p"},"content":[{"type":"text","text":"Hot"}]}]},
          {"type":"toggleBlock","attrs":{"blockId":"g1","open":false},"content":[
            {"type":"paragraph","attrs":{"blockId":"g1p"},"content":[{"type":"text","text":"Summary"}]},
            {"type":"paragraph","attrs":{"blockId":"g1q"},"content":[{"type":"text","text":"Hidden"}]}]},
          {"type":"columnBlock","content":[
            {"type":"column","attrs":{"width":2},"content":[
              {"type":"paragraph","attrs":{"blockId":"l1"},"content":[{"type":"text","text":"Left"}]}]},
            {"type":"column","attrs":{"width":1},"content":[
              {"type":"paragraph","attrs":{"blockId":"r1"},"content":[{"type":"text","text":"Right"}]}]}]},
          {"type":"horizontalRule","attrs":{"blockId":"d1"}},
          {"type":"image","attrs":{"blockId":"i1","src":"heracles-image://local/abc","alt":"Map","title":null}}
        ]}
        """

    private func read(_ json: String) -> EditorDocument {
        var ids = BlockIDs.sequential()
        return EditorDocument(json: json, ids: &ids)!
    }

    @Test func readsEveryKindOfBlock() {
        var document = read(everything)
        // The columns and their rows have no stored ids. Give them fixed ones to print.
        let row = document.blocks.firstIndex { $0.kind == .columns }!
        document.blocks[row].id = "cols"
        document.blocks[row].children[0].id = "left"
        document.blocks[row].children[1].id = "right"
        #expect(outline(document) == """
            h2#h1 Trip plan
            p#p1 Pack the lantern
            bullet#b1 Tent
              bullet#b2 Pegs
              p#b1q More about the tent
            bullet#b3 Stove
            numbered#n1 Third
            done#t1 Book ferry
            todo#t2
            quote#q1
              p#q1p Quoted
            code#c1 fn main() {}
            callout#k1
              p#k1p Hot
            toggle(closed)#g1
              p#g1p Summary
              p#g1q Hidden
            columns#cols
              column#left
                p#l1 Left
              column#right
                p#r1 Right
            divider#d1
            image#i1
            """)
        #expect(shape(document.blocks[1].text) == "[Pack the ][b:lantern]")
        #expect(document.blocks[2].paragraphID == "b1p")
        #expect(document.block("c1")?.language == "rust")
        #expect(document.block("k1")?.emoji == "🔥")
        #expect(document.block("left")?.width == 2)
        #expect(document.block("i1")?.attrs["alt"] == .string("Map"))
        #expect(document.block("n1")?.listAttrs["start"] == .number(3))
    }

    @Test func writesADocumentBackAsItWasStored() {
        #expect(sameJSON(read(everything).json, everything))
    }

    @Test func twoListsInARowStayTwoLists() {
        let stored = """
            {"type":"doc","content":[
              {"type":"bulletList","content":[{"type":"listItem","attrs":{"blockId":"a"},"content":[
                {"type":"paragraph","attrs":{"blockId":"ap"},"content":[{"type":"text","text":"A"}]}]}]},
              {"type":"bulletList","content":[{"type":"listItem","attrs":{"blockId":"b"},"content":[
                {"type":"paragraph","attrs":{"blockId":"bp"},"content":[{"type":"text","text":"B"}]}]}]}]}
            """
        let document = read(stored)
        #expect(outline(document) == "bullet#a A\n^bullet#b B")
        #expect(sameJSON(document.json, stored))
    }

    @Test func listItemsOfOneKindAreWrittenAsOneList() {
        let document = doc(bullet("a", "A"), bullet("b", "B"), todo("c", "C"), p("d", "D"), bullet("e", "E"))
        #expect(sameJSON(document.json, """
            {"type":"doc","content":[
              {"type":"bulletList","content":[
                {"type":"listItem","attrs":{"blockId":"a"},"content":[{"type":"paragraph","attrs":{"blockId":"a-p"},"content":[{"type":"text","text":"A"}]}]},
                {"type":"listItem","attrs":{"blockId":"b"},"content":[{"type":"paragraph","attrs":{"blockId":"b-p"},"content":[{"type":"text","text":"B"}]}]}]},
              {"type":"taskList","content":[
                {"type":"taskItem","attrs":{"blockId":"c","checked":false},"content":[{"type":"paragraph","attrs":{"blockId":"c-p"},"content":[{"type":"text","text":"C"}]}]}]},
              {"type":"paragraph","attrs":{"blockId":"d"},"content":[{"type":"text","text":"D"}]},
              {"type":"bulletList","content":[
                {"type":"listItem","attrs":{"blockId":"e"},"content":[{"type":"paragraph","attrs":{"blockId":"e-p"},"content":[{"type":"text","text":"E"}]}]}]}]}
            """))
    }

    @Test func aListItemsChildrenAreWrittenInsideIt() {
        let document = doc(numbered("a", "A", [p("x", "more"), numbered("b", "B")]))
        #expect(sameJSON(document.json, """
            {"type":"doc","content":[{"type":"orderedList","content":[
              {"type":"listItem","attrs":{"blockId":"a"},"content":[
                {"type":"paragraph","attrs":{"blockId":"a-p"},"content":[{"type":"text","text":"A"}]},
                {"type":"paragraph","attrs":{"blockId":"x"},"content":[{"type":"text","text":"more"}]},
                {"type":"orderedList","content":[{"type":"listItem","attrs":{"blockId":"b"},"content":[
                  {"type":"paragraph","attrs":{"blockId":"b-p"},"content":[{"type":"text","text":"B"}]}]}]}]}]}]}
            """))
    }

    @Test func blocksWithoutIdsAreGivenThem() {
        let document = read("""
            {"type":"doc","content":[
              {"type":"paragraph","content":[{"type":"text","text":"A"}]},
              {"type":"bulletList","content":[{"type":"listItem","content":[{"type":"paragraph"}]}]}]}
            """)
        #expect(outline(document) == "p#n1 A\nbullet#n2")
        #expect(document.blocks[1].paragraphID == "n3")
    }

    @Test func anIdUsedTwiceIsKeptOnlyByTheFirst() {
        let document = read("""
            {"type":"doc","content":[
              {"type":"paragraph","attrs":{"blockId":"same"},"content":[{"type":"text","text":"A"}]},
              {"type":"paragraph","attrs":{"blockId":"same"},"content":[{"type":"text","text":"B"}]}]}
            """)
        #expect(outline(document) == "p#same A\np#n1 B")
    }

    @Test func anEmptyDocumentIsOneEmptyParagraph() {
        #expect(outline(read(#"{"type":"doc"}"#)) == "p#n1")
        #expect(outline(read(#"{"type":"doc","content":[]}"#)) == "p#n1")
    }

    @Test func whatIsNotADocumentIsRefused() {
        var ids = BlockIDs.sequential()
        #expect(EditorDocument(json: "not json", ids: &ids) == nil)
        #expect(EditorDocument(json: #"{"type":"paragraph"}"#, ids: &ids) == nil)
    }

    @Test func aBlockThisEditorDoesNotKnowIsKeptAsStored() {
        let stored = """
            {"type":"doc","content":[
              {"type":"table","attrs":{"blockId":"x"},"content":[{"type":"tableRow"}]},
              {"type":"paragraph","attrs":{"blockId":"p","textAlign":"center"},"content":[{"type":"text","text":"A"}]}]}
            """
        let document = read(stored)
        #expect(outline(document) == "unknown#x\np#p A")
        // So is an attribute it does not know, on a block it does.
        #expect(sameJSON(document.json, stored))
    }

    @Test func aCodeBlockStoresItsTextAsAnAttribute() {
        #expect(sameJSON(doc(code("c", "let x = 1\nlet y = 2", language: "swift")).json, """
            {"type":"doc","content":[{"type":"codeMirrorBlock","attrs":{"blockId":"c","language":"swift","code":"let x = 1\\nlet y = 2"}}]}
            """))
    }

    @Test func aContainerLeftEmptyIsWrittenWithAParagraphSoItStaysValid() {
        let written = JSONValue(text: doc(quote("q", [])).json)!
        let inside = written["content"]?.arrayValue?.first?["content"]?.arrayValue
        #expect(inside?.count == 1)
        #expect(inside?.first?["type"] == .string("paragraph"))
    }

    @Test func idsCanBeMadeInSequenceForTests() {
        var ids = BlockIDs.sequential(prefix: "x")
        #expect(ids.make() == "x1")
        #expect(ids.make() == "x2")
        var random = BlockIDs.random
        let first = random.make()
        #expect(first.count == 36 && first != random.make())
        #expect(first == first.lowercased())
    }
}
