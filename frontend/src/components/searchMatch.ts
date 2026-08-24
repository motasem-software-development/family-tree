/**
 * Text matching for the searchable selects. Both halves of this app's audience type in a
 * script the other one cannot: an Arabic speaker looking for فلسطين will not reach for the
 * shift key, and an English speaker hunting Türkiye will type "turkiye". Comparing raw
 * strings would fail both, so everything is folded to a common form first.
 */

/** Tashkeel, superscript alef, and tatweel — decoration that carries no search meaning. */
const ARABIC_MARKS = /[ً-ْٰـ]/g

/** Combining marks left behind by NFD, so "Côte" folds onto "cote". */
const COMBINING_MARKS = /[̀-ͯ]/g

/** Anything that is neither a letter nor a digit: "+970" and "970" must be the same query. */
const NON_ALPHANUMERIC = /[^\p{L}\p{N}]/gu

/** ٠١٢٣٤٥٦٧٨٩ and ۰۱۲۳۴۵۶۷۸۹ both mean 0-9 to someone typing a dialing code. */
const foldDigits = (text: string): string =>
  text.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660))
      .replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0))

/**
 * The comparable form of a string: lower case, unaccented, undecorated, and stripped of
 * punctuation.
 *
 * The Arabic letter folding is deliberately lossy. Hamza placement (أ/إ/آ vs ا), final ة vs ه,
 * and ى vs ي are the spellings people disagree about most, and a picker that demands the
 * catalog's exact choice is a picker that looks broken. Folding them together costs nothing
 * here — the worst case is one extra row in a filtered list.
 */
export const fold = (text: string): string =>
  foldDigits(text)
    .normalize('NFD')
    .replace(COMBINING_MARKS, '')
    .replace(ARABIC_MARKS, '')
    .replace(/[أإآٱ]/g, 'ا')
    .replace(/ى/g, 'ي')
    .replace(/ة/g, 'ه')
    .replace(/[ؤئ]/g, 'ء')
    .replace(NON_ALPHANUMERIC, '')
    .toLowerCase()

/**
 * Whether any of `haystacks` contains `query`. Substring rather than prefix: someone looking
 * for the United Arab Emirates is as likely to type "emirates" as "united".
 *
 * An empty query matches everything, so an untouched picker shows the whole list.
 */
export const matches = (query: string, haystacks: readonly string[]): boolean => {
  const needle = fold(query)
  if (needle === '') return true
  return haystacks.some((haystack) => fold(haystack).includes(needle))
}
