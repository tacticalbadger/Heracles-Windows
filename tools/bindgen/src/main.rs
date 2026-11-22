//! UniFFI's Swift bindings generator, pinned to the version heracles-core
//! builds against so the two can never disagree. Run by `macos/build-core.sh`.

fn main() {
    uniffi::uniffi_bindgen_swift()
}
