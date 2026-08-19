// Strip tags (and their attributes — e.g. base64 image src payloads) from note
// HTML so searches match only what the user actually reads.
export const stripHtml = (value: string) => value.replace(/<[^>]*>/g, ' ');
