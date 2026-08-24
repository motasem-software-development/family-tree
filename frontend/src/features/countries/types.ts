/** One country of residence, as returned by `GET /api/v1/countries`. */
export interface Country {
  id: number
  /** ISO 3166-1 alpha-2, upper case. The flag emoji is derived from this. */
  code: string
  nameAr: string
  nameEn: string
  /** E.164 dialing code, leading `+`. Not unique — US and CA are both `+1`. */
  dialCode: string
}
