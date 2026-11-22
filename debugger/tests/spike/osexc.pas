program osexc;                                 {  1 }
                                               {  2 }
procedure Boom;                                {  3 }
begin                                          {  4 }
  RaiseException($E0504153, 0, 0, nil);        {  5 }
end;                                           {  6 }
                                               {  7 }
begin                                          {  8 }
  Boom;                                        {  9 }
end.                                           { 10 }
