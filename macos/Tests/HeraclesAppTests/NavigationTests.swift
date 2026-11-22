import Foundation
import Testing

@testable import HeraclesApp

struct NavigationPolicyTests {
    private func decide(_ string: String) -> NavigationDecision {
        NavigationPolicy.decide(URL(string: string))
    }

    @Test func webLinksOpenInTheBrowser() {
        #expect(decide("https://example.com/a") == .openExternally)
        #expect(decide("http://example.com") == .openExternally)
        #expect(decide("mailto:someone@example.com") == .openExternally)
    }

    @Test func everythingElseIsRefused() {
        #expect(decide("file:///tmp/evil.html") == .deny)
        #expect(decide("javascript:alert(1)") == .deny)
        #expect(decide("data:text/html,<script>alert(1)</script>") == .deny)
        #expect(decide("heracles-image://local/abc") == .deny, "an image is not a place to go")
        #expect(decide("heracles://elsewhere/abc") == .deny, "not a link to a note")
        #expect(decide("about:blank") == .deny)
        #expect(NavigationPolicy.decide(nil) == .deny)
    }

    @Test func appLinksAreHandedToTheApp() {
        #expect(decide("heracles://note/abc") == .openInApp)
    }
}

struct AppLinkTests {
    @Test func aNoteLinkNamesItsNote() {
        #expect(AppLink(URL(string: "heracles://note/abc-123")!) == AppLink(noteID: "abc-123", blockID: nil))
    }

    @Test func aBlockLinkNamesItsBlockToo() {
        let link = AppLink(URL(string: "heracles://note/abc-123#block=b-9")!)
        #expect(link == AppLink(noteID: "abc-123", blockID: "b-9"))
    }

    @Test func otherURLsAreNotAppLinks() {
        for string in ["https://note/abc", "heracles://note/", "heracles://other/abc", "heracles://note/a/b"] {
            #expect(AppLink(URL(string: string)!) == nil, "\(string)")
        }
    }

    @Test func aLinkWritesBackToTheSameURL() {
        #expect(AppLink(noteID: "abc", blockID: nil).url.absoluteString == "heracles://note/abc")
        #expect(AppLink(noteID: "abc", blockID: "b1").url.absoluteString == "heracles://note/abc#block=b1")
    }
}
