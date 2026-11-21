program Gallery;

uses
  Forms, AlignForm, AnchorForm, ContainerForm, IntrinsicForm;

begin
  Application.Initialize;
  Application.Title := 'Gallery';
  Application.CreateForm(TAlignForm, AlignForm.AlignForm);
  Application.CreateForm(TAnchorForm, AnchorForm.AnchorForm);
  Application.CreateForm(TContainerForm, ContainerForm.ContainerForm);
  Application.CreateForm(TIntrinsicForm, IntrinsicForm.IntrinsicForm);
  Application.Run;
end.
