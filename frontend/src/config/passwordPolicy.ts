/**
 * Mirrors PasswordPolicy.MinimumLength in src/FamilyTree.Application/Auth/PasswordPolicy.cs.
 *
 * The server is the enforcement point; this exists only so the user-visible strings that quote
 * the number are not written out as literals in each locale file — where changing the C#
 * constant would leave both languages silently lying with no test failing. Interpolated into
 * every message through i18n's interpolation.defaultVariables, so the value appears exactly
 * once in the frontend.
 *
 * The fully correct fix is for the server to supply its minimum (an RFC 7807 Problem Details
 * extension on PASSWORD_TOO_SHORT), which would remove this mirror entirely. Out of scope here.
 */
export const PASSWORD_MINIMUM_LENGTH = 12
