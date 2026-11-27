// covers: LANG-790, CMP-1395, CMP-1396, CMP-1397, CMP-1398
program ClassCtorPerInstance;

{ the class constructor of every distinct instance runs once per image, in the first module in initialization order that
  defines the instance (URegA for TRegistry<Integer> and TRegistry<string>, URegB for TRegistry<Byte>); the nested
  TEntry of an instance is an instance of its own and runs after it. Every later defining module (URegB, the program)
  skips what already ran; the class destructors run in the finalization of the module that ran the class constructor. }

uses
  UReg, URegA, URegB;

begin
  Writeln('main');
  Writeln('Runs = ', Runs);
  Writeln('Count = ', TRegistry<Integer>.Count, ' ', TRegistry<string>.Count, ' ', TRegistry<Byte>.Count);
  Writeln('Entries = ', TRegistry<Integer>.TEntry.Hits, ' ', TRegistry<string>.TEntry.Hits, ' ', TRegistry<Byte>.TEntry.Hits);
  Writeln('A uses ', CountA, ', B uses ', CountB);
end.
