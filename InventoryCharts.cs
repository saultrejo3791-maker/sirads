using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public sealed class InventoryCharts : UserControl
{
    private readonly DatabaseService _db;
    private readonly bool _medications;
    private readonly ComboBox _article=new(){MinWidth=290,MaxWidth=430,DisplayMemberPath="Name",Margin=new Thickness(0,0,10,8)};
    private readonly ComboBox _period=new(){ItemsSource=new[]{"Semana","Mes","Año"},SelectedIndex=0,MinWidth=120,Margin=new Thickness(0,0,10,8)};
    private readonly DatePicker _date=new(){SelectedDate=DateTime.Today,Width=155,Margin=new Thickness(0,0,10,8)};
    private readonly TextBlock _caption=CenterUi.Text("");
    private readonly WrapPanel _summary=new();
    private readonly StackPanel _series=new();
    private readonly StackPanel _stock=new();
    private List<InventoryArticle> _articles=new();
    private List<InventoryUse> _uses=new();
    public InventoryCharts(DatabaseService db,bool medications)
    {
        _db=db;_medications=medications;var root=new StackPanel{Margin=new Thickness(24)};
        var title=CenterUi.Text(medications?"Medicamentos · existencias y consumo":"Alimentos y limpieza · existencias y consumo",25);title.FontWeight=FontWeights.SemiBold;root.Children.Add(title);
        root.Children.Add(CenterUi.Text("Selecciona un artículo y un período. Las gráficas se calculan con los movimientos registrados."));
        var filters=new WrapPanel();filters.Children.Add(_article);filters.Children.Add(_period);filters.Children.Add(_date);filters.Children.Add(CenterUi.Button("Actualizar",Reload));root.Children.Add(filters);
        var nav=new WrapPanel();nav.Children.Add(CenterUi.Button("‹ Período anterior",()=>Move(-1)));nav.Children.Add(CenterUi.Button("Hoy",()=>_date.SelectedDate=DateTime.Today));nav.Children.Add(CenterUi.Button("Período siguiente ›",()=>Move(1)));root.Children.Add(nav);
        root.Children.Add(_caption);root.Children.Add(_summary);root.Children.Add(Card("CONSUMO REGISTRADO",_series));root.Children.Add(CenterUi.Text("Promedio diario = salidas de uso / días del período transcurridos, incluyendo hoy. En períodos completos se cuentan todos los días. Cero significa que no hay consumo registrado. Las bajas se muestran aparte."));
        root.Children.Add(Card("EXISTENCIA ACTUAL POR ARTÍCULO",_stock));root.Children.Add(CenterUi.Text("Las barras de existencias se comparan únicamente dentro de la misma unidad. La existencia es la actual, aunque consultes consumo de períodos anteriores."));
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        _article.SelectionChanged+=(_,_)=>Draw();_period.SelectionChanged+=(_,_)=>Draw();_date.SelectedDateChanged+=(_,_)=>Draw();Reload();
    }
    private static Border Card(string title,StackPanel content)
    {
        var panel=new StackPanel();var label=CenterUi.Text(title,12);label.FontWeight=FontWeights.SemiBold;panel.Children.Add(label);panel.Children.Add(content);return new Border{Child=panel,Background=Brushes.White,BorderBrush=CenterUi.Brush("Line"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(20),Margin=new Thickness(0,12,0,12)};
    }
    private void Move(int direction)
    {
        var date=_date.SelectedDate??DateTime.Today;_date.SelectedDate=(_period.SelectedItem as string) switch{"Mes"=>date.AddMonths(direction),"Año"=>date.AddYears(direction),_=>date.AddDays(direction*7)};
    }
    public void Reload()
    {
        try
        {
            var id=(_article.SelectedItem as InventoryArticle)?.Id;
            if(_medications)
            {
                _articles=_db.GetMedications().Select(m=>new InventoryArticle(m.Id,m.DisplayName,m.Unit,m.Stock,m.MinimumStock,"Medicamentos")).ToList();
                _uses=_db.GetMedicationMovements().Where(m=>m.Type is MedicationMovementTypes.Supply or MedicationMovementTypes.Loss).Select(m=>new InventoryUse(m.MedicationId,m.OccurredAt,m.Quantity,m.Type==MedicationMovementTypes.Loss)).ToList();
            }
            else
            {
                _articles=_db.GetPantryItems().Select(i=>new InventoryArticle(i.Id,i.Name+" · "+i.Category,i.Unit,i.Stock,i.Minimum,i.Category)).ToList();
                _uses=_db.GetPantryMovements().Where(m=>m.Type is "Salida" or "Baja").Select(m=>new InventoryUse(m.ItemId,m.OccurredAt,m.Quantity,m.Type=="Baja")).ToList();
            }
            _article.ItemsSource=_articles;_article.SelectedItem=_articles.FirstOrDefault(a=>a.Id==id)??_articles.FirstOrDefault();Draw();DrawStock();
        }catch(Exception ex){_caption.Text="No se pudieron cargar las gráficas: "+ex.Message;}
    }
    private void Draw()
    {
        _series.Children.Clear();_summary.Children.Clear();if(_article.SelectedItem is not InventoryArticle article){_caption.Text="Aún no hay artículos registrados. Agrega artículos y movimientos para ver sus gráficas.";return;}
        var report=InventoryAnalytics.Calculate(article.Id,_uses,_period.SelectedItem as string??"Semana",_date.SelectedDate??DateTime.Today,DateTime.Now);
        _caption.Text=$"{report.Start:dd/MM/yyyy} – {report.End.AddDays(-1):dd/MM/yyyy} · {article.Unit} · {report.MeasuredDays} día(s) medido(s)";
        Metric("Existencia actual",$"{article.Stock:0.###} {article.Unit}");Metric("Consumo del período",$"{report.Consumed:0.###} {article.Unit}");Metric("Consumo por día",$"{report.DailyRate:0.###} {article.Unit}");Metric("Bajas del período",$"{report.Losses:0.###} {article.Unit}");
        var max=report.Bars.Max(b=>b.Quantity);if(max==0)_series.Children.Add(CenterUi.Text("Sin consumo registrado en este período."));
        foreach(var b in report.Bars) _series.Children.Add(Bar(b.Label,b.Quantity,max,article.Unit,false));
    }
    private void Metric(string label,string value)
    {
        var panel=new StackPanel{Width=180};panel.Children.Add(CenterUi.Text(label,12));var text=CenterUi.Text(value,20);text.FontWeight=FontWeights.SemiBold;text.Foreground=CenterUi.Brush("Green");panel.Children.Add(text);_summary.Children.Add(new Border{Child=panel,Padding=new Thickness(12),CornerRadius=new CornerRadius(10),Background=CenterUi.Brush("Mint"),Margin=new Thickness(0,4,10,4)});
    }
    private void DrawStock()
    {
        _stock.Children.Clear();if(_articles.Count==0){_stock.Children.Add(CenterUi.Text("No hay existencias para graficar."));return;}
        foreach(var group in _articles.GroupBy(a=>a.Unit).OrderBy(g=>g.Key))
        {
            var label=CenterUi.Text("Unidad: "+group.Key,15);label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,16,0,10);_stock.Children.Add(label);var max=group.Max(a=>a.Stock);
            foreach(var a in group.OrderByDescending(a=>a.Stock))_stock.Children.Add(Bar(a.Name,a.Stock,max,a.Unit,a.Stock<=a.Minimum));
        }
    }
    private static Grid Bar(string label,decimal quantity,decimal maximum,string unit,bool low)
    {
        var row=new Grid{Margin=new Thickness(0,5,0,5)};row.ColumnDefinitions.Add(new(){Width=new GridLength(225)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(135)});
        var name=CenterUi.Text(label,12);name.Margin=new Thickness(0,0,12,0);row.Children.Add(name);
        var track=new Grid{Height=14,VerticalAlignment=VerticalAlignment.Center,ClipToBounds=true};track.Children.Add(new Border{CornerRadius=new CornerRadius(7),Background=CenterUi.Brush("Surface")});
        var fill=new Border{CornerRadius=new CornerRadius(7),Background=low?new SolidColorBrush(Color.FromRgb(181,130,48)):CenterUi.Brush("Green"),HorizontalAlignment=HorizontalAlignment.Left};
        track.SizeChanged+=(_,_)=>fill.Width=maximum==0?0:track.ActualWidth*(double)(quantity/maximum);track.Children.Add(fill);Grid.SetColumn(track,1);row.Children.Add(track);
        var value=CenterUi.Text($"{quantity:0.###} {unit}"+(low?" · bajo":""),12);value.TextAlignment=TextAlignment.Right;value.Margin=new Thickness(10,0,0,0);Grid.SetColumn(value,2);row.Children.Add(value);return row;
    }
    public static void Open(Window owner,DatabaseService db,bool medications)
    {
        new Window{Title="Inventario · Gráficas y consumo",Owner=owner,Width=1040,Height=800,MinWidth=940,MinHeight=580,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=CenterUi.Brush("Surface"),Content=new InventoryCharts(db,medications)}.ShowDialog();
    }
}
