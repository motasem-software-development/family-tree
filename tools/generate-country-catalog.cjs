const fs = require('fs')


const DIAL = {
AF:'+93',AL:'+355',DZ:'+213',AS:'+1684',AD:'+376',AO:'+244',AI:'+1264',AG:'+1268',AR:'+54',AM:'+374',
AW:'+297',AU:'+61',AT:'+43',AZ:'+994',BS:'+1242',BH:'+973',BD:'+880',BB:'+1246',BY:'+375',BE:'+32',
BZ:'+501',BJ:'+229',BM:'+1441',BT:'+975',BO:'+591',BQ:'+599',BA:'+387',BW:'+267',BR:'+55',VG:'+1284',
BN:'+673',BG:'+359',BF:'+226',BI:'+257',CV:'+238',KH:'+855',CM:'+237',CA:'+1',KY:'+1345',CF:'+236',
TD:'+235',CL:'+56',CN:'+86',CO:'+57',KM:'+269',CG:'+242',CD:'+243',CK:'+682',CR:'+506',CI:'+225',
HR:'+385',CU:'+53',CW:'+599',CY:'+357',CZ:'+420',DK:'+45',DJ:'+253',DM:'+1767',DO:'+1809',EC:'+593',
EG:'+20',SV:'+503',GQ:'+240',ER:'+291',EE:'+372',SZ:'+268',ET:'+251',FK:'+500',FO:'+298',FJ:'+679',
FI:'+358',FR:'+33',GF:'+594',PF:'+689',GA:'+241',GM:'+220',GE:'+995',DE:'+49',GH:'+233',GI:'+350',
GR:'+30',GL:'+299',GD:'+1473',GP:'+590',GU:'+1671',GT:'+502',GG:'+44',GN:'+224',GW:'+245',GY:'+592',
HT:'+509',HN:'+504',HK:'+852',HU:'+36',IS:'+354',IN:'+91',ID:'+62',IR:'+98',IQ:'+964',IE:'+353',
IM:'+44',IL:'+972',IT:'+39',JM:'+1876',JP:'+81',JE:'+44',JO:'+962',KZ:'+7',KE:'+254',KI:'+686',
KP:'+850',KR:'+82',KW:'+965',KG:'+996',LA:'+856',LV:'+371',LB:'+961',LS:'+266',LR:'+231',LY:'+218',
LI:'+423',LT:'+370',LU:'+352',MO:'+853',MG:'+261',MW:'+265',MY:'+60',MV:'+960',ML:'+223',MT:'+356',
MH:'+692',MQ:'+596',MR:'+222',MU:'+230',YT:'+262',MX:'+52',FM:'+691',MD:'+373',MC:'+377',MN:'+976',
ME:'+382',MS:'+1664',MA:'+212',MZ:'+258',MM:'+95',NA:'+264',NR:'+674',NP:'+977',NL:'+31',NC:'+687',
NZ:'+64',NI:'+505',NE:'+227',NG:'+234',NU:'+683',NF:'+672',MK:'+389',MP:'+1670',NO:'+47',OM:'+968',
PK:'+92',PW:'+680',PS:'+970',PA:'+507',PG:'+675',PY:'+595',PE:'+51',PH:'+63',PL:'+48',PT:'+351',
PR:'+1787',QA:'+974',RE:'+262',RO:'+40',RU:'+7',RW:'+250',BL:'+590',SH:'+290',KN:'+1869',LC:'+1758',
MF:'+590',PM:'+508',VC:'+1784',WS:'+685',SM:'+378',ST:'+239',SA:'+966',SN:'+221',RS:'+381',SC:'+248',
SL:'+232',SG:'+65',SX:'+1721',SK:'+421',SI:'+386',SB:'+677',SO:'+252',ZA:'+27',SS:'+211',ES:'+34',
LK:'+94',SD:'+249',SR:'+597',SJ:'+47',SE:'+46',CH:'+41',SY:'+963',TW:'+886',TJ:'+992',TZ:'+255',
TH:'+66',TL:'+670',TG:'+228',TK:'+690',TO:'+676',TT:'+1868',TN:'+216',TR:'+90',TM:'+993',TC:'+1649',
TV:'+688',UG:'+256',UA:'+380',AE:'+971',GB:'+44',US:'+1',UY:'+598',UZ:'+998',VU:'+678',VA:'+39',
VE:'+58',VN:'+84',WF:'+681',EH:'+212',YE:'+967',ZM:'+260',ZW:'+263',AX:'+358',XK:'+383',
}


// ICU renders these three in a form this app should not ship. PS is the one that matters:
// "الأراضي الفلسطينية / Palestinian Territories" is not what this family calls home. SA and AE
// are ICU's formal long names where the curated list used the short ones a dropdown wants.
const PINNED = {
  PS: ['فلسطين', 'Palestine'],
  SA: ['السعودية', 'Saudi Arabia'],
  AE: ['الإمارات', 'United Arab Emirates'],
}

const ar = new Intl.DisplayNames(['ar'], { type: 'region' })
const en = new Intl.DisplayNames(['en'], { type: 'region' })

const rows = Object.keys(DIAL).sort().map((code) => {
  const [nameAr, nameEn] = PINNED[code] ?? [ar.of(code), en.of(code)]
  if (!/^\+[1-9]\d{0,3}$/.test(DIAL[code])) throw new Error(`bad dial code for ${code}`)
  if (nameAr.length > 100 || nameEn.length > 100) throw new Error(`name too long for ${code}`)
  if (nameAr.includes('"') || nameEn.includes('"')) throw new Error(`quote in name for ${code}`)
  return `        ("${code}", "${nameAr}", "${nameEn}", "${DIAL[code]}")`
})

const file = `namespace FamilyTree.Infrastructure.Persistence.Seed;

/// <summary>
/// Every ISO 3166-1 alpha-2 country and inhabited territory, with its E.164 dialing code.
/// Uninhabited territories (Antarctica, Bouvet Island, the Heard and McDonald Islands and the
/// like) are omitted: nobody resides there, so they would be noise in a country-of-residence
/// picker.
///
/// Names are CLDR's, generated once rather than fetched, so the list cannot drift at runtime
/// and needs no external service. Three are overridden by hand — CLDR calls PS "الأراضي
/// الفلسطينية / Palestinian Territories", which is not what this family calls home, and gives
/// SA and AE their formal long names where the short ones read better in a dropdown.
///
/// Note that DialCode is NOT unique — +1 covers the US, Canada and much of the Caribbean, and
/// +44 covers the UK, Jersey, Guernsey and the Isle of Man. Only Code is unique.
///
/// Regenerate with tools/generate-country-catalog.cjs. Adding a country by hand is fine too:
/// the seeder inserts only codes the database does not already have, so it stays idempotent.
/// </summary>
public static class CountryCatalog
{
    public static IReadOnlyList<(string Code, string NameAr, string NameEn, string DialCode)> All { get; } =
    [
${rows.join(',\n')}
    ];
}
`

fs.writeFileSync(process.argv[2], file, 'utf8')
console.log(`wrote ${rows.length} countries`)
