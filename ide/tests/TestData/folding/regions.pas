unit Regions;

interface

{$REGION 'Public types'}
type
  TA = Integer;
{$ENDREGION}

{$region}
const
  C = 1;
  {$REGION 'Inner'}
  D = 2;
  {$ENDREGION}
{$endregion}

{$ENDREGION}

implementation

end.
