using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public sealed class PantryView : UserControl
{
    private readonly DatabaseService _db;
    private readonly string _user;
    private readonly TextBox _search=new(){MinWidth=200,Margin=new Thickness(0,0,10,8)};
    private readonly ComboBox _category=new(){ItemsSource=new[]{"Todas las categorías","Alimentos","Limpieza"},SelectedIndex=0,MinWidth=175,Margin=new Thickness(0,0,10,8),Padding=new Thickness(8)};
    private readonly CheckBox _low=new(){Content="Solo existencia baja",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,8)};
    private readonly TextBlock _summary=CenterUi.Text("");
    private readonly DataGrid _grid=CenterUi.Table(("Artículo","Name",230),("Categoría","Category",120),("Existencia","Stock",110),("Unidad","Unit",95),("Mínimo","Minimum",95),("Estado","Status",140),("Ubicación","Location",190));
    private List<PantryItem> _items=new();
    public PantryView(DatabaseService db,string user)
    {
        _db=db;_user=user;var root=new Grid();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var toolbar=new WrapPanel();toolbar.Children.Add(CenterUi.Button("+ Nuevo artículo",()=>Edit(null),true));toolbar.Children.Add(CenterUi.Button("Gráficas y consumo",()=>InventoryCharts.Open(Window.GetWindow(this),_db,false)));toolbar.Children.Add(CenterUi.Button("Historial de movimientos",History));toolbar.Children.Add(CenterUi.Button("Actualizar",Refresh));root.Children.Add(CenterUi.Frame(toolbar));
        var filters=new WrapPanel();filters.Children.Add(CenterUi.Text("Buscar:"));filters.Children.Add(_search);filters.Children.Add(_category);filters.Children.Add(_low);Grid.SetRow(filters,1);root.Children.Add(filters);
        Grid.SetRow(_grid,2);root.Children.Add(_grid);var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};footer.Children.Add(_summary);var actions=new WrapPanel();actions.Children.Add(CenterUi.Button("Registrar entrada",()=>Movement("Entrada"),true));actions.Children.Add(CenterUi.Button("Registrar salida",()=>Movement("Salida")));actions.Children.Add(CenterUi.Button("Registrar baja",()=>Movement("Baja")));actions.Children.Add(CenterUi.Button("Editar artículo",()=>{if(_grid.SelectedItem is PantryItem i)Edit(i);else _summary.Text="Selecciona un artículo.";}));footer.Children.Add(actions);Grid.SetRow(footer,3);root.Children.Add(footer);Content=root;
        _search.TextChanged+=(_,_)=>Filter();_category.SelectionChanged+=(_,_)=>Filter();_low.Checked+=(_,_)=>Filter();_low.Unchecked+=(_,_)=>Filter();_grid.MouseDoubleClick+=(_,_)=>{if(_grid.SelectedItem is PantryItem i)Edit(i);};Refresh();
    }
    public void Refresh(){try{_items=_db.GetPantryItems();Filter();}catch(Exception ex){_summary.Text=ex.Message;}}
    private void Filter()
    {
        var items=_items.Where(i=>(_category.SelectedIndex==0||i.Category==_category.SelectedItem as string)&&($"{i.Name} {i.Location}".Contains(_search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase))&&(_low.IsChecked!=true||i.Stock<=i.Minimum)).ToList();_grid.ItemsSource=items;
        _summary.Text=$"{items.Count} artículo(s) · {_items.Count(i=>i.Stock<=i.Minimum)} con existencia baja o agotada. Selecciona un artículo para registrar movimientos.";
    }
    private void Edit(PantryItem? existing)
    {
        var item=existing==null?new PantryItem():new PantryItem{Id=existing.Id,Name=existing.Name,Category=existing.Category,Unit=existing.Unit,Minimum=existing.Minimum,Location=existing.Location,Version=existing.Version};
        var form=new CenterForm(existing==null?"Nuevo artículo":"Editar artículo","Las existencias se modifican mediante entradas y salidas. Usa punto decimal, sin separadores de miles.",v=>{item.Name=v["name"];item.Category=v["category"];item.Unit=v["unit"];item.Location=v["location"];item.Minimum=CenterForm.Number(v["minimum"]);_db.SavePantryItem(item,_user);}){Owner=Window.GetWindow(this)};
        form.Add(new("name","Nombre del artículo",true),item.Name);form.Add(new("category","Categoría",true,"choice",["Alimentos","Limpieza"]),item.Category,existing==null);form.Add(new("unit","Unidad de inventario (kg, litro, pieza...)",true),item.Unit,existing==null);form.Add(new("minimum","Existencia mínima",true),item.Minimum.ToString(CultureInfo.InvariantCulture));form.Add(new("location","Ubicación"),item.Location);if(form.ShowDialog()==true)Refresh();
    }
    private void Movement(string type)
    {
        if(_grid.SelectedItem is not PantryItem item){_summary.Text="Selecciona primero un artículo.";return;}
        var id=Guid.NewGuid().ToString();var form=new CenterForm("Registrar "+type.ToLowerInvariant(),$"{item.Name} · Disponible: {item.Stock:0.###} {item.Unit}. El movimiento queda en el historial.",v=>_db.RecordPantryMovement(id,item.Id,type,CenterForm.Number(v["quantity"]),v["reference"],v["responsible"],_user)){Owner=Window.GetWindow(this)};
        form.Add(new("quantity",$"Cantidad ({item.Unit}; hasta 3 decimales con punto)",true));form.Add(new("reference",type=="Entrada"?"Origen / proveedor / referencia":type=="Baja"?"Motivo de baja (pérdida, caducidad, daño...)":"Destino / uso",true,"memo"));form.Add(new("responsible","Responsable",true),_user);if(form.ShowDialog()==true)Refresh();
    }
    private void History()
    {
        try
        {
            var all=_db.GetPantryMovements();var root=new DockPanel{Margin=new Thickness(20)};var search=new TextBox{ToolTip="Buscar artículo, categoría, referencia o responsable"};DockPanel.SetDock(search,Dock.Top);root.Children.Add(search);
            var table=CenterUi.Table(("Fecha y hora","OccurredAt",160),("Artículo","Item",210),("Categoría","Category",120),("Movimiento","Type",110),("Cantidad","Quantity",95),("Saldo","Balance",95),("Unidad","Unit",95),("Referencia","Reference",240),("Responsable","Responsible",170),("Registró","RecordedBy",160));root.Children.Add(table);table.ItemsSource=all;
            search.TextChanged+=(_,_)=>table.ItemsSource=all.Where(m=>$"{m.Item} {m.Category} {m.Reference} {m.Responsible}".Contains(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)).ToList();
            new Window{Title="Inventario · Historial de entradas y salidas",Owner=Window.GetWindow(this),Width=1000,Height=650,MinWidth=650,MinHeight=450,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=root,Background=CenterUi.Brush("Surface")}.ShowDialog();
        }catch(Exception ex){_summary.Text=ex.Message;}
    }
}
