// covers: RTL-070, RTL-072
program Sys_StrRoutines;
{$POINTERMATH ON}
{ system.md section 5.3, every row with its clipping cases: Copy, Pos, Delete, Insert, UpCase, StringOfChar,
  SetString, the header accessors, and the PWideChar constructors of rtl-abi.md section 9.3. }
var
  Base: NativeInt;

function Raw(const S: string): Pointer;
begin
  Result := PPointer(@S)^;
end;

procedure Show(const Name, S: string);
begin
  Writeln(Name, ' [', S, '] ', Length(S));
end;

procedure Run;
var
  S, T: string;
  Buf: array[0..5] of WideChar;
  P: PWideChar;
  Q: Pointer;
  C: Char;
begin
  S := 'Hello, World';
  Writeln('# Copy');
  Show('1,5', Copy(S, 1, 5));
  Show('8,100', Copy(S, 8, 100));
  Show('8', Copy(S, 8));
  Show('0,3', Copy(S, 0, 3));
  Show('-5,3', Copy(S, -5, 3));
  Show('12,1', Copy(S, 12, 1));
  Show('13,2', Copy(S, 13, 2));
  Show('5,0', Copy(S, 5, 0));
  Show('5,-1', Copy(S, 5, -1));
  Show('empty', Copy(T, 1, 3));
  T := Copy(S, 1, MaxInt);
  Writeln('whole copy is new ', Raw(T) <> Raw(S), ' ', StringRefCount(T), ' ', StringRefCount(S), ' ', T);
  T := Copy(T, 3, 4);
  Show('into its source', T);

  Writeln('# Pos');
  S := 'abcabc';
  Writeln(Pos('b', S), ' ', Pos('bc', S, 3), ' ', Pos('abc', S, 2), ' ', Pos('x', S), ' ', Pos('', S), ' ',
    Pos('abcabcd', S), ' ', Pos('c', S, 7), ' ', Pos('c', S, 6), ' ', Pos('a', S, 0), ' ', Pos('c', S, -3), ' ',
    Pos('a', T), ' ', Pos('abcabc', S), ' ', Pos('A', S));

  Writeln('# Delete');
  S := 'Hello, World';
  T := S;
  Delete(T, 6, 2);
  Show('6,2', T);
  Show('source intact', S);
  T := S;
  Delete(T, 6, 100);
  Show('6,100', T);
  T := S;
  Delete(T, 0, 3);
  Show('0,3', T);
  Delete(T, 13, 1);
  Show('13,1', T);
  Delete(T, 12, 1);
  Show('12,1', T);
  Delete(T, 1, 0);
  Show('1,0', T);
  Delete(T, 1, -2);
  Show('1,-2', T);
  Delete(T, 1, 11);
  Writeln('all deleted nil ', Raw(T) = nil);
  T := 'literal';
  Delete(T, 1, 3);
  Writeln('from literal [', T, '] ', StringRefCount(T));

  Writeln('# Insert');
  T := 'ace';
  Insert('b', T, 2);
  Show('2', T);
  Insert('d', T, 4);
  Show('4', T);
  Insert('!', T, 100);
  Show('past end', T);
  Insert('>', T, 0);
  Show('0', T);
  Insert('<', T, -5);
  Show('-5', T);
  Insert('', T, 3);
  Show('empty source', T);
  C := '#';
  Insert(C, T, 1);
  Show('char', T);
  Insert(T, T, 2);
  Show('into itself', T);
  T := '';
  Insert('xy', T, 5);
  Show('into empty', T);
  S := T;
  Insert('-', T, 2);
  Writeln('shared [', S, '] [', T, '] ', StringRefCount(S), ' ', StringRefCount(T));

  Writeln('# UpCase');
  Writeln(UpCase('a'), UpCase('m'), UpCase('z'), UpCase('A'), UpCase('1'), UpCase('{'), UpCase('`'), ' ',
    Ord(UpCase(#233)), ' ', Ord(UpCase(#$3B1)));

  Writeln('# StringOfChar');
  Show('3', StringOfChar('x', 3));
  Show('1', StringOfChar(#$263A, 1));
  Show('0', StringOfChar('x', 0));
  Show('-1', StringOfChar('x', -1));
  T := StringOfChar('-', 40);
  Writeln(T, ' ', StringRefCount(T), ' ', PWord(Raw(T))[40]);
  T := StringOfChar('=', 5);
  Show('reassigned', T);

  Writeln('# SetString');
  Buf[0] := 'p';
  Buf[1] := 'q';
  Buf[2] := 'r';
  Buf[3] := 's';
  Buf[4] := #0;
  SetString(S, @Buf[0], 3);
  Writeln('[', S, '] ', Length(S), ' ', StringRefCount(S), ' ', PWord(Raw(S))[3]);
  SetString(S, @Buf[1], 0);
  Writeln('0 is nil ', Raw(S) = nil);
  SetString(S, @Buf[1], -4);
  Writeln('-4 is nil ', Raw(S) = nil);
  SetString(S, nil, 4);
  Writeln('nil buffer ', Length(S), ' ', StringRefCount(S), ' ', PWord(Raw(S))[4]);
  T := S;
  SetString(S, @Buf[2], 2);
  Writeln('replaces [', S, '] ', Length(T), ' ', StringRefCount(T));

  Writeln('# accessors');
  S := 'lit';
  T := S + S;
  Writeln(StringRefCount(S), ' ', StringRefCount(T), ' ', StringCodePage(T), ' ', StringElementSize(T), ' ',
    StringRefCount(''), ' ', StringCodePage(''), ' ', StringElementSize(''));

  Writeln('# helpers');
  P := @Buf[0];
  _UStrFromPWChar(PPointer(@S)^, P);
  Show('FromPWChar', S);
  _UStrFromPWChar(PPointer(@S)^, nil);
  Writeln('FromPWChar nil ', Raw(S) = nil);
  Buf[0] := #0;
  _UStrFromPWChar(PPointer(@S)^, P);
  Writeln('FromPWChar empty ', Raw(S) = nil);
  _UStrFromPWCharLen(PPointer(@S)^, @Buf[1], 2);
  Show('FromPWCharLen', S);
  Q := _UStrNew(3);
  Writeln('New ', PInteger(PByte(Q) - 16)^, ' ', PWord(Q)[3], ' ', _UStrNew(0) = nil, ' ', _UStrNew(-1) = nil);
  _UStrClr(Q);
  Writeln('Clr ', Q = nil);
end;

begin
  Base := _LiveAllocCount;
  Run;
  Writeln('all released ', _LiveAllocCount - Base);
end.
