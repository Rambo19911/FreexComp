// covers: LANG-263, ABI-060, ABI-063, ABI-064, CMP-306, CMP-309
program RecordLayout;
type
  TA = record
    B: Byte;
    I: Integer;
    W: Word;
  end;
  TP = packed record
    B: Byte;
    I: Integer;
    W: Word;
  end;
  TD = record
    B: Byte;
    D: Int64;
  end;
  TE = record
  end;
  TN = record
    B: Byte;
    A: TA;
    C: Char;
  end;
{$A4}
  T4 = record
    B: Byte;
    D: Int64;
  end;
{$ALIGN 2}
  T2 = record
    B: Byte;
    I: Integer;
  end;
{$A-}
  T1 = record
    B: Byte;
    I: Integer;
  end;
{$A+}
  TArr = record
    B: Byte;
    V: array[1..3] of Word;
  end;
  TPN = packed record
    B: Byte;
    A: TA;
  end;
  TBig = record
    C: Char;
    P: Pointer;
    S: set of 0..9;
  end;
  TIn4 = record
    C: Byte;
    I: T4;
  end;
  TPs = array[1..3] of TP;
  T4s = array[0..1] of T4;
var
  VP, WP: TP;
  V4: T4;
  V1, W1: T1;
  VPN, WPN: TPN;
  VI: TIn4;
  Ps: TPs;
  E, F: TE;
begin
  Writeln(SizeOf(TA), ' ', SizeOf(TP), ' ', SizeOf(TD), ' ', SizeOf(TE), ' ', SizeOf(TN));
  Writeln(SizeOf(T4), ' ', SizeOf(T2), ' ', SizeOf(T1), ' ', SizeOf(TArr), ' ', SizeOf(TPN), ' ', SizeOf(TBig));
  Writeln(SizeOf(TIn4), ' ', SizeOf(TPs), ' ', SizeOf(T4s), ' ', SizeOf(VPN.A), ' ', SizeOf(E));
  VP.B := 1;
  VP.I := -2;
  VP.W := 65535;
  WP := VP;
  VP.I := 3;
  Writeln(WP.B, ' ', WP.I, ' ', WP.W, ' ', VP.I);
  V4.B := 7;
  V4.D := -9000000000;
  Writeln(V4.B, ' ', V4.D);
  V1.B := 255;
  V1.I := 123456789;
  W1 := V1;
  Writeln(W1.B, ' ', W1.I);
  VPN.B := 9;
  VPN.A.B := 8;
  VPN.A.I := 70000;
  VPN.A.W := 6;
  WPN := VPN;
  Writeln(WPN.B, ' ', WPN.A.B, ' ', WPN.A.I, ' ', WPN.A.W);
  VI.C := 1;
  VI.I.B := 2;
  VI.I.D := 3;
  Writeln(VI.C, ' ', VI.I.B, ' ', VI.I.D);
  Ps[1] := VP;
  Ps[2] := WP;
  Ps[3].I := Ps[1].I + Ps[2].I;
  Writeln(Ps[1].I, ' ', Ps[2].W, ' ', Ps[3].I);
  E := F;
  Writeln('done');
end.
