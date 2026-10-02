using System.Globalization;
using Calendar = System.Windows.Controls.Calendar;
using System.Windows;
using System.Windows.Controls;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public sealed class CenterRecordsView : UserControl
{
    private readonly DatabaseService _db;
    private readonly RecordModule _module;
    private readonly string _user;
    private readonly TextBox _search=new(){Margin=new Thickness(0,0,12,8),MinWidth=220};
    private readonly DataGrid _table;
    private readonly TextBlock _summary=CenterUi.Text("");
    private readonly TextBlock _empty=CenterUi.Text("No hay registros para los filtros seleccionados.",16);
    private readonly Calendar? _calendar;
    private readonly ListBox? _monthAgenda;
    private List<CenterRecord> _records=new();
    private DateTime? _day;
    public CenterRecordsView(DatabaseService db,string kind,string user)
    {
        _db=db;_module=CenterModules.Get(kind);_user=user;
        var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var toolbar=new WrapPanel();_search.ToolTip="Buscar por paciente, título o contenido";_search.MinWidth=260;toolbar.Children.Add(_search);toolbar.Children.Add(CenterUi.Button("+ Nuevo registro",()=>Edit(null),true));toolbar.Children.Add(CenterUi.Button("Actualizar",Refresh));root.Children.Add(CenterUi.Frame(toolbar));_search.TextChanged+=(_,_)=>Filter();
        if(kind=="Calendario")
        {
            var calendarRow=new Grid{Margin=new Thickness(0,0,0,12)};calendarRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});calendarRow.ColumnDefinitions.Add(new());
            _calendar=new Calendar{DisplayDate=DateTime.Today,SelectedDate=DateTime.Today,HorizontalAlignment=HorizontalAlignment.Left};_day=DateTime.Today;
            _calendar.SelectedDatesChanged+=(_,_)=>{_day=_calendar.SelectedDate;Filter();};_calendar.DisplayDateChanged+=(_,_)=>RefreshMonth();calendarRow.Children.Add(_calendar);
            var right=new StackPanel{Margin=new Thickness(24,0,0,0)};right.Children.Add(CenterUi.Text("AGENDA DEL MES",15));_monthAgenda=new ListBox{Height=115,BorderThickness=new Thickness(0),Background=CenterUi.Brush("Surface"),DisplayMemberPath="Label"};right.Children.Add(_monthAgenda);_monthAgenda.SelectionChanged+=(_,_)=>{if(_monthAgenda.SelectedItem is AgendaDay day){_calendar.SelectedDate=day.Date;_calendar.DisplayDate=day.Date;}};
            var actions=new WrapPanel();actions.Children.Add(CenterUi.Button("Ver todas las fechas",()=>{_day=null;_calendar.SelectedDate=null;Filter();}));actions.Children.Add(CenterUi.Button("Hoy",()=>{_calendar.SelectedDate=DateTime.Today;_calendar.DisplayDate=DateTime.Today;}));right.Children.Add(actions);Grid.SetColumn(right,1);calendarRow.Children.Add(right);Grid.SetRow(calendarRow,1);root.Children.Add(calendarRow);
        }
        _table=CenterUi.Table((kind=="Traslados"?"Programado":"Fecha y hora","OccurredAt",155),("Paciente","PatientName",205),("Título / motivo","Title",220),("Estado","Status",125),("Responsable","Responsible",170));
        if(kind=="Calendario") {AddColumn("Tipo","Type",135);AddColumn("Monto previsto","Amount",130);}
        if(kind=="Traslados") {AddColumn("Dirección","Address",270);AddColumn("Solicitud registrada","CreatedAt",165);}
        var listPanel=new Grid();listPanel.Children.Add(_table);_empty.HorizontalAlignment=HorizontalAlignment.Center;_empty.VerticalAlignment=VerticalAlignment.Center;_empty.IsHitTestVisible=false;listPanel.Children.Add(_empty);Grid.SetRow(listPanel,2);root.Children.Add(listPanel);
        var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};footer.Children.Add(_summary);var buttons=new WrapPanel();buttons.Children.Add(CenterUi.Button("Ver / editar seleccionado",()=>Edit(_table.SelectedItem as CenterRecord,true)));buttons.Children.Add(CenterUi.Button("Historial de cambios",ShowHistory));footer.Children.Add(buttons);Grid.SetRow(footer,3);root.Children.Add(footer);Content=root;
        _table.MouseDoubleClick+=(_,_)=>{if(_table.SelectedItem is CenterRecord record)Edit(record);};Refresh();
    }
    private void AddColumn(string title,string binding,int width) => _table.Columns.Add(new DataGridTextColumn{Header=title,Binding=new System.Windows.Data.Binding(binding){StringFormat=binding.EndsWith("At")?"dd/MM/yyyy HH:mm":null},Width=width,ElementStyle=(Style)Application.Current.FindResource("GridText")});
    public void Refresh()
    {
        try{_records=_db.GetCenterRecords(_module.Key);Filter();RefreshMonth();}catch(Exception ex){_summary.Text=ex.Message;}
    }
    private void RefreshMonth()
    {
        if(_calendar==null||_monthAgenda==null)return;var month=_calendar.DisplayDate;
        _monthAgenda.ItemsSource=_records.Where(r=>r.OccurredAt.Year==month.Year&&r.OccurredAt.Month==month.Month).GroupBy(r=>r.OccurredAt.Date).OrderBy(g=>g.Key).Select(g=>new AgendaDay(g.Key,$"{g.Key:dd/MM} · {g.Count()} registro(s) · {g.Count(r=>r.Type=="Fecha de pago"&&r.Status=="Pendiente")} pago(s) pendiente(s)")).ToList();
    }
    private void Filter()
    {
        if(_table==null)return;var query=_search.Text.Trim();var list=_records.Where(r=>(_day==null||r.OccurredAt.Date==_day)&&($"{r.PatientName} {r.Title} {r.Responsible} {string.Join(" ",r.Fields.Values)}".Contains(query,StringComparison.CurrentCultureIgnoreCase))).OrderBy(r=>r.OccurredAt).ToList();
        _table.ItemsSource=list;_empty.Visibility=list.Count==0?Visibility.Visible:Visibility.Collapsed;
        _summary.Text=$"{list.Count} registro(s)"+(_day.HasValue?$" · {_day:dd/MM/yyyy}":" · Todas las fechas")+" · Las modificaciones conservan su historial.";
    }
    private void Edit(CenterRecord? existing,bool requireSelection=false)
    {
        if(existing==null&&requireSelection){_summary.Text="Selecciona un registro de la lista.";return;}
        var record=existing?.Copy()??new CenterRecord{Kind=_module.Key,Responsible=_user,OccurredAt=_day?.AddHours(9)??DateTime.Now};
        var form=new CenterForm(existing==null?$"Nuevo · {_module.Title}":$"Editar · {_module.Title}","Los campos con * son obligatorios. Cada cambio conserva fecha, usuario y versión anterior.", values=>
        {
            record.Title=values["title"];record.PatientId=string.IsNullOrWhiteSpace(values["patient"])?null:values["patient"];record.Responsible=values["responsible"];
            if(!DateTime.TryParseExact(values["when"],"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var when))throw new InvalidOperationException("Completa la fecha y hora.");record.OccurredAt=when;
            record.Fields=_module.Fields.ToDictionary(f=>f.Key,f=>values[f.Key]);_db.SaveCenterRecord(record,_user);
        }){Owner=Window.GetWindow(this)};
        form.Add(new("title","Título / motivo breve",true),record.Title);form.AddPatients(_db.GetPatients(),record.PatientId,_module.PatientRequired);
        form.Add(new("when",_module.Key=="Traslados"?"Fecha y hora programadas":"Fecha y hora",true,"datetime"),record.OccurredAt.ToString("yyyy-MM-dd HH:mm"));
        form.Add(new("responsible","Responsable del registro / atención",true),record.Responsible);
        foreach(var field in _module.Fields)form.Add(field,record.Fields.GetValueOrDefault(field.Key,""));
        if(form.ShowDialog()==true)Refresh();
    }
    private void ShowHistory()
    {
        if(_table.SelectedItem is not CenterRecord record){_summary.Text="Selecciona un registro para consultar sus versiones.";return;}
        try
        {
            var versions=_db.GetCenterHistory(record.Id);var text=string.Join("\n\n────────────────────────\n\n",versions.Select(r=>$"VERSIÓN {r.Version} · {r.UpdatedAt:dd/MM/yyyy HH:mm} · {r.UpdatedBy}\n{r.Title}\nPaciente: {r.PatientName}\nFecha del registro: {r.OccurredAt:dd/MM/yyyy HH:mm}\nResponsable: {r.Responsible}\nCreado: {r.CreatedAt:dd/MM/yyyy HH:mm} · {r.CreatedBy}\n\n"+string.Join("\n\n",_module.Fields.Select(f=>$"{f.Label}:\n{r.Fields.GetValueOrDefault(f.Key,"—")}"))));
            CenterUi.ShowText(Window.GetWindow(this),"Historial · "+record.Title,text);
        }catch(Exception ex){_summary.Text=ex.Message;}
    }
    private sealed record AgendaDay(DateTime Date,string Label);
}
