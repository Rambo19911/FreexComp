// phase: 8
// covers: RTL-1217
program NetEnc_HtmlEntities;
{ NetEncoding, the named character references Decode knows (rtl/phase8/encodings-and-hashes.md section 3.5, vectors
  6.2 H): the 252 names of HTML 4.01 section 24 in the order of that document - 96 for Latin-1, 124 symbols and
  Greek letters, 32 markup-significant and internationalization characters - and "apos". Each name decodes to
  exactly its one character, alone and inside text; without its ";" it is copied; with the case of its first letter
  flipped it is copied unless that spelling is a name of its own; the character survives Encode and Decode.
  The code points are those of the HTML Living Standard. It differs from HTML 4.01 for two names: "lang" and "rang"
  are U+27E8 and U+27E9 there (HTML 4.01: U+2329, U+232A). }
uses
  SysUtils, Classes, NetEncoding, TestKit;

const
  Count = 253;
  Names: array[0..Count - 1] of string = (
    'nbsp', 'iexcl', 'cent', 'pound', 'curren', 'yen', 'brvbar', 'sect', 'uml', 'copy', 'ordf', 'laquo', 'not', 'shy',
    'reg', 'macr', 'deg', 'plusmn', 'sup2', 'sup3', 'acute', 'micro', 'para', 'middot', 'cedil', 'sup1', 'ordm',
    'raquo', 'frac14', 'frac12', 'frac34', 'iquest', 'Agrave', 'Aacute', 'Acirc', 'Atilde', 'Auml', 'Aring', 'AElig',
    'Ccedil', 'Egrave', 'Eacute', 'Ecirc', 'Euml', 'Igrave', 'Iacute', 'Icirc', 'Iuml', 'ETH', 'Ntilde', 'Ograve',
    'Oacute', 'Ocirc', 'Otilde', 'Ouml', 'times', 'Oslash', 'Ugrave', 'Uacute', 'Ucirc', 'Uuml', 'Yacute', 'THORN',
    'szlig', 'agrave', 'aacute', 'acirc', 'atilde', 'auml', 'aring', 'aelig', 'ccedil', 'egrave', 'eacute', 'ecirc',
    'euml', 'igrave', 'iacute', 'icirc', 'iuml', 'eth', 'ntilde', 'ograve', 'oacute', 'ocirc', 'otilde', 'ouml',
    'divide', 'oslash', 'ugrave', 'uacute', 'ucirc', 'uuml', 'yacute', 'thorn', 'yuml', 'fnof', 'Alpha', 'Beta',
    'Gamma', 'Delta', 'Epsilon', 'Zeta', 'Eta', 'Theta', 'Iota', 'Kappa', 'Lambda', 'Mu', 'Nu', 'Xi', 'Omicron', 'Pi',
    'Rho', 'Sigma', 'Tau', 'Upsilon', 'Phi', 'Chi', 'Psi', 'Omega', 'alpha', 'beta', 'gamma', 'delta', 'epsilon',
    'zeta', 'eta', 'theta', 'iota', 'kappa', 'lambda', 'mu', 'nu', 'xi', 'omicron', 'pi', 'rho', 'sigmaf', 'sigma',
    'tau', 'upsilon', 'phi', 'chi', 'psi', 'omega', 'thetasym', 'upsih', 'piv', 'bull', 'hellip', 'prime', 'Prime',
    'oline', 'frasl', 'weierp', 'image', 'real', 'trade', 'alefsym', 'larr', 'uarr', 'rarr', 'darr', 'harr', 'crarr',
    'lArr', 'uArr', 'rArr', 'dArr', 'hArr', 'forall', 'part', 'exist', 'empty', 'nabla', 'isin', 'notin', 'ni',
    'prod', 'sum', 'minus', 'lowast', 'radic', 'prop', 'infin', 'ang', 'and', 'or', 'cap', 'cup', 'int', 'there4',
    'sim', 'cong', 'asymp', 'ne', 'equiv', 'le', 'ge', 'sub', 'sup', 'nsub', 'sube', 'supe', 'oplus', 'otimes',
    'perp', 'sdot', 'lceil', 'rceil', 'lfloor', 'rfloor', 'lang', 'rang', 'loz', 'spades', 'clubs', 'hearts', 'diams',
    'quot', 'amp', 'lt', 'gt', 'OElig', 'oelig', 'Scaron', 'scaron', 'Yuml', 'circ', 'tilde', 'ensp', 'emsp',
    'thinsp', 'zwnj', 'zwj', 'lrm', 'rlm', 'ndash', 'mdash', 'lsquo', 'rsquo', 'sbquo', 'ldquo', 'rdquo', 'bdquo',
    'dagger', 'Dagger', 'permil', 'lsaquo', 'rsaquo', 'euro', 'apos');
  Codes: array[0..Count - 1] of Word = (
    160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182,
    183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205,
    206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228,
    229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251,
    252, 253, 254, 255, 402, 913, 914, 915, 916, 917, 918, 919, 920, 921, 922, 923, 924, 925, 926, 927, 928, 929, 931,
    932, 933, 934, 935, 936, 937, 945, 946, 947, 948, 949, 950, 951, 952, 953, 954, 955, 956, 957, 958, 959, 960, 961,
    962, 963, 964, 965, 966, 967, 968, 969, 977, 978, 982, 8226, 8230, 8242, 8243, 8254, 8260, 8472, 8465, 8476, 8482,
    8501, 8592, 8593, 8594, 8595, 8596, 8629, 8656, 8657, 8658, 8659, 8660, 8704, 8706, 8707, 8709, 8711, 8712, 8713,
    8715, 8719, 8721, 8722, 8727, 8730, 8733, 8734, 8736, 8743, 8744, 8745, 8746, 8747, 8756, 8764, 8773, 8776, 8800,
    8801, 8804, 8805, 8834, 8835, 8836, 8838, 8839, 8853, 8855, 8869, 8901, 8968, 8969, 8970, 8971, 10216, 10217,
    9674, 9824, 9827, 9829, 9830, 34, 38, 60, 62, 338, 339, 352, 353, 376, 710, 732, 8194, 8195, 8201, 8204, 8205,
    8206, 8207, 8211, 8212, 8216, 8217, 8218, 8220, 8221, 8222, 8224, 8225, 8240, 8249, 8250, 8364, 39);

var
  H: TNetEncoding;

{ the place of N in Names, or -1 }
function IndexOfName(const N: string): Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := Count - 1 downto 0 do
    if Names[I] = N then
      Result := I;
end;

{ N with the case of its first letter changed }
function FlipFirst(const N: string): string;
begin
  Result := N;
  if (N[1] >= 'a') and (N[1] <= 'z') then
    Result[1] := Char(Ord(N[1]) - 32)
  else
    Result[1] := Char(Ord(N[1]) + 32);
end;

{ what is wrong with the name at I, or '' }
function Fault(I: Integer): string;
var
  N, C, F: string;
  J: Integer;
begin
  N := Names[I];
  C := Char(Codes[I]);
  F := FlipFirst(N);
  J := IndexOfName(F);
  Result := '';
  if H.Decode('&' + N + ';') <> C then
    Result := 'does not decode to its character'
  else if H.Decode('x&' + N + ';y&' + N + ';') <> 'x' + C + 'y' + C then
    Result := 'is not decoded inside text'
  else if H.Decode('&' + N) <> '&' + N then
    Result := 'is decoded without its ";"'
  else if H.Decode('&' + N + ' ;&' + N + '&') <> '&' + N + ' ;&' + N + '&' then
    Result := 'is decoded although another character follows it'
  else if H.Decode('&#' + IntToStr(Integer(Codes[I])) + ';&#x' + IntToHex(Integer(Codes[I]), 1) + ';') <> C + C then
    Result := 'differs from the numeric reference to its code point'
  else if H.Decode(H.Encode(C)) <> C then
    Result := 'its character does not survive Encode and Decode'
  else if (J < 0) and (H.Decode('&' + F + ';') <> '&' + F + ';') then
    Result := 'is decoded with the case of its first letter flipped'
  else if (J >= 0) and (H.Decode('&' + F + ';') <> Char(Codes[J])) then
    Result := 'its flipped spelling, a name of its own, decodes wrongly';
end;

{ the number of names from First to Last that have a fault; each one is reported }
function Faults(First, Last: Integer): Integer;
var
  I: Integer;
  What: string;
begin
  Result := 0;
  for I := First to Last do
  begin
    What := Fault(I);
    if What <> '' then
    begin
      Inc(Result);
      Check(False, '&' + Names[I] + '; ' + What);
    end;
  end;
end;

procedure Run;
var
  I, Distinct, Twins, InOrder: Integer;
  Coded, Plain, S: string;
begin
  Section('the names by group (RTL-1217)');
  CheckEqI(0, Faults(0, 95), 'Latin-1: 96 names');
  CheckEqI(0, Faults(96, 219), 'symbols, mathematical symbols and Greek letters: 124 names');
  CheckEqI(0, Faults(220, 251), 'markup-significant and internationalization characters: 32 names');
  CheckEqI(0, Faults(252, 252), 'added: apos');

  Section('the list itself');
  Distinct := 0;
  Twins := 0;
  InOrder := 0;
  Coded := '';
  Plain := '';
  for I := 0 to Count - 1 do
  begin
    if IndexOfName(Names[I]) = I then
      Inc(Distinct);
    if IndexOfName(FlipFirst(Names[I])) >= 0 then
      Inc(Twins);
    if (I <= 95) and (Codes[I] = 160 + I) then
      Inc(InOrder);
    Coded := Coded + '&' + Names[I] + ';';
    Plain := Plain + Char(Codes[I]);
  end;
  CheckEqI(253, High(Names) - Low(Names) + 1, 'names');
  CheckEqI(253, Distinct, 'different names');
  CheckEqI(96, InOrder, 'the Latin-1 names stand for U+00A0 to U+00FF in order');
  CheckEqI(110, Twins, 'names whose first letter may have either case (two names each)');
  S := H.Decode(Coded);
  CheckEqI(253, Length(S), 'all names in one text decode to 253 characters');
  Check(S = Plain, 'each of them the character of its name');
  Check(H.Decode(H.Encode(Plain)) = Plain, 'and the 253 characters survive Encode and Decode');

  Section('code points of the Living Standard; names outside the list');
  Check(H.Decode('&lang;&rang;') = #$27E8#$27E9, 'lang and rang are U+27E8 and U+27E9');
  Check(H.Decode('&thetasym;&alefsym;&there4;&frac34;&sup3;') = #$03D1#$2135#$2234#$00BE#$00B3, 'long names and names with digits');
  S := '&thetasymx;&thetasy;&bigstar;&dollar;&Tab;&NewLine;&lbrace;&COPY;&REG;&QUOT;&GT;&nbsp1;&1nbsp;&Apos;&APOS;';
  CheckEqS(S, H.Decode(S), 'names of other lists and near misses are copied');
end;

var
  Before: NativeInt;
begin
  H := TNetEncoding.HTML;
  { the shared instance lives until the program ends: it exists before the count is taken }
  H.Decode(H.Encode('x'));
  Before := _LiveAllocCount;
  Run;
  CheckEqI(Before, _LiveAllocCount, 'no block is left');
  Done;
end.
