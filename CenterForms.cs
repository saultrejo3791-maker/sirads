using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SIRASD.Desktop.Models;

namespace SIRASD.Desktop;

internal static class CenterUi
{
    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    public static TextBlock Text(string text,int size=13) => new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush("Ink"),Margin=new Thickness(0,0,0,8)};
    public static Button Button(string title,Action action,bool primary=false)
    {
        var b=new Button{Content=title,Margin=new Thickness(0,0,8,8)};if(primary)b.SetResourceReference(FrameworkElement.StyleProperty,"PrimaryButton");b.Click+=(_,_)=>action();return b;
    }
    public static Border Frame(UIElement content) => new(){Child=content,Background=Brushes.White,BorderBrush=Brush("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(14,14,6,6),Margin=new Thickness(0,0,0,14)};
    public static DataGrid Table(params (string title,string binding,int width)[] columns)
    {
        var grid=new DataGrid();foreach(var (title,binding,width) in columns)grid.Columns.Add(new DataGridTextColumn{Header=title,Binding=new Binding(binding){StringFormat=binding.EndsWith("At")?"dd/MM/yyyy HH:mm":null},Width=width,ElementStyle=(Style)Application.Current.FindResource("GridText")});return grid;
    }
    public static void ShowText(Window owner,string title,string text)
    {
        var box=new TextBox{Text=text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(20),VerticalContentAlignment=VerticalAlignment.Top};
        new Window{Title=title,Owner=owner,Width=800,Height=680,MinWidth=550,MinHeight=400,Content=box,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Brush("Surface")}.ShowDialog();
    }
}

internal sealed class CenterForm : Window
{
    private readonly StackPanel _fields=new();
    private readonly Dictionary<string,Func<string>> _read=new();
    private readonly TextBlock _error=CenterUi.Text("");
    public readonly Dictionary<string,Control> Controls=new();
    public CenterForm(string title,string description,Action<Dictionary<string,string>> save)
    {
        Title=title;Width=730;Height=790;MinWidth=550;MinHeight=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=CenterUi.Brush("Surface");
        var grid=new Grid();grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new());grid.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new StackPanel{Margin=new Thickness(24,20,24,12)};var heading=CenterUi.Text(title,25);heading.FontWeight=FontWeights.SemiBold;header.Children.Add(heading);header.Children.Add(CenterUi.Text(description));grid.Children.Add(header);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new Border{Child=_fields,Background=Brushes.White,BorderBrush=CenterUi.Brush("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(20)},Padding=new Thickness(24,0,24,12)};Grid.SetRow(scroll,1);grid.Children.Add(scroll);
        var footer=new StackPanel{Margin=new Thickness(24,12,24,16)};_error.Foreground=Brushes.Firebrick;footer.Children.Add(_error);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(CenterUi.Button("Cancelar",()=>DialogResult=false));
        var saveButton=CenterUi.Button("Guardar",()=>{try{save(_read.ToDictionary(x=>x.Key,x=>x.Value()));DialogResult=true;}catch(Exception ex){_error.Text=ex.Message;}} ,true);actions.Children.Add(saveButton);footer.Children.Add(actions);Grid.SetRow(footer,2);grid.Children.Add(footer);Content=grid;
    }
    public void Add(RecordField field,string value="",bool enabled=true)
    {
        var label=CenterUi.Text(field.Label+(field.Required?" *":""));label.FontWeight=FontWeights.SemiBold;_fields.Children.Add(label);
        Control control;
        if(field.Type=="password")
        {
            var password=new PasswordBox{MaxLength=128};control=password;_read[field.Key]=()=>password.Password;
        }
        else if(field.Type=="choice")
        {
            var choices=(field.Options??[]).ToList();if(value.Length>0&&!choices.Contains(value))choices.Add(value);
            var combo=new ComboBox{ItemsSource=choices,SelectedItem=value.Length>0?value:choices.FirstOrDefault(),MinHeight=43,Padding=new Thickness(9)};control=combo;_read[field.Key]=()=>combo.SelectedItem as string??"";
        }
        else if(field.Type=="date" || field.Type=="datetime")
        {
            DateTime? initial=DateTime.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed)?parsed:null;
            var date=new DatePicker{SelectedDate=initial};date.DateValidationError+=(_,e)=>e.ThrowException=false;
            if(field.Type=="datetime")
            {
                var row=new Grid();row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(140)});var time=new TextBox{Text=initial?.ToString("HH:mm")??"",ToolTip="Hora de 24 horas: HH:mm",Margin=new Thickness(12,0,0,0)};date.Margin=new Thickness(0);row.Children.Add(date);Grid.SetColumn(time,1);row.Children.Add(time);_fields.Children.Add(row);
                _read[field.Key]=()=> {if(string.IsNullOrWhiteSpace(date.Text)&&string.IsNullOrWhiteSpace(time.Text))return "";if(!DateTime.TryParse(date.Text,CultureInfo.CurrentCulture,DateTimeStyles.None,out var day)||!TimeOnly.TryParseExact(time.Text.Trim(),"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var hour))throw new InvalidOperationException($"Revisa fecha y hora (HH:mm): {field.Label}.");return day.ToString("yyyy-MM-dd")+" "+hour.ToString("HH:mm");};
                _fields.Children.Add(CenterUi.Text("Hora de 24 horas, por ejemplo 14:30."));Controls[field.Key]=date;return;
            }
            control=date;_read[field.Key]=()=> {if(string.IsNullOrWhiteSpace(date.Text))return "";if(!DateTime.TryParse(date.Text,CultureInfo.CurrentCulture,DateTimeStyles.None,out var day))throw new InvalidOperationException($"Revisa la fecha: {field.Label}.");return day.ToString("yyyy-MM-dd");};
        }
        else
        {
            var text=new TextBox{Text=value,Margin=new Thickness(0),AcceptsReturn=field.Type=="memo",TextWrapping=TextWrapping.Wrap,VerticalContentAlignment=VerticalAlignment.Top,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};if(field.Type=="memo")text.Height=110;
            control=text;_read[field.Key]=()=>text.Text.Trim();
        }
        control.IsEnabled=enabled;control.Margin=new Thickness(0,0,0,16);System.Windows.Automation.AutomationProperties.SetName(control,field.Label);Controls[field.Key]=control;_fields.Children.Add(control);
    }
    public void AddPatients(IEnumerable<Patient> patients,string? selectedId,bool required)
    {
        _fields.Children.Add(CenterUi.Text("Paciente"+(required?" *":" (opcional)")));
        var options=new List<PatientChoice>{new("","Sin paciente vinculado")};options.AddRange(patients.Select(p=>new PatientChoice(p.Id,p.Folio+" · "+p.FullName)));
        var combo=new ComboBox{ItemsSource=options,DisplayMemberPath="Label",SelectedValuePath="Id",SelectedValue=selectedId??"",IsEditable=true,IsTextSearchEnabled=true,MinHeight=43,Margin=new Thickness(0,0,0,16)};
        _fields.Children.Add(combo);Controls["patient"]=combo;_read["patient"]=()=>{if(combo.SelectedItem is not PatientChoice option)throw new InvalidOperationException("Selecciona un paciente de la lista.");return option.Id;};
    }
    private sealed record PatientChoice(string Id,string Label);
    public static decimal Number(string text)
    {
        if(!decimal.TryParse(text,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var n))throw new InvalidOperationException("Escribe una cantidad válida con punto decimal y sin separadores de miles.");return n;
    }
}
