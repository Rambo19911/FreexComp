// covers: LANG-711, LANG-716, CMP-385
unit GenZ;

interface

type
  TBase<T> = class
  public
    function Get: T; virtual;
    property Current: T read Get;
  end;

  TBoxZ<A> = class
    First: A;
  end;

  TDerived<T> = class(TBase<T>)
  public
    procedure Take(const Other: TBase<T>);
  end;

implementation

function TBase<T>.Get: T;
begin
  Result := Default(T);
end;

procedure TDerived<T>.Take(const Other: TBase<T>);
begin
  Other.Get;
end;

end.
