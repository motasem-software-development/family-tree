// `vitest.setup.ts` lives outside `src/` and is therefore not part of the TS program that
// `tsc -b` type-checks for the production build (see tsconfig.app.json's `include: ["src"]`).
// Vitest itself doesn't need this file — it runs tests via esbuild without type-checking —
// but `npm run build` does type-check test files under `src/`, and without this reference
// they cannot see the jest-dom matchers (`toBeInTheDocument`, `toHaveTextContent`, etc.)
// that `vitest.setup.ts` registers on Vitest's `Assertion` interface at runtime.
/// <reference types="@testing-library/jest-dom/vitest" />
