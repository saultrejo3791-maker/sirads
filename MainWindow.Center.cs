using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SIRASD.Desktop.Models;
using SIRASD.Desktop.Services;

namespace SIRASD.Desktop;

public partial class MainWindow
{
    private string _currentEmail="";
    private readonly List<Button> _centerNavigation=new();
    private void InitializeCenterNavigation()
    {
        foreach(var module in CenterModules.All)AddCenterNavigation(module.Key,module.Title);
        AddCenterNavigation("Inventario","Alimentos y limpieza");
        AddCenterNavigation("Centro","Centro y cuentas");
    }
    private void AddCenterNavigation(string key,string label)
    {
        var text=new TextBlock{Text=label,Width=114,ToolTip=label,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
        var glyph=key switch {"Incidencias"=>"\uE7BA","Calendario"=>"\uE787","Consejeria"=>"\uE8F2","Historias"=>"\uE8A5","Examenes"=>"\uE9D9","Psiquiatria"=>"\uE95E","Traslados"=>"\uE806",_=>"\uE7B8"};
        var content=new StackPanel{Orientation=Orientation.Horizontal};content.Children.Add(new TextBlock{Text=System.Text.RegularExpressions.Regex.Unescape(glyph),FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),FontSize=16,Margin=new Thickness(0,0,12,0),VerticalAlignment=VerticalAlignment.Center});content.Children.Add(text);
        var b=new Button{Content=content,Tag=key,Style=(Style)FindResource("NavigationButton"),Padding=new Thickness(16,9,12,9),MinHeight=38};
        b.Click+=Navigation_Click;ExtraNavigation.Children.Add(b);_centerNavigation.Add(b);
    }
    private void ShowCenterPanel(string page)
    {
        var module=CenterModules.All.FirstOrDefault(m=>m.Key==page);
        CenterPanel.Visibility=module!=null||page=="Inventario"||page=="Centro"?Visibility.Visible:Visibility.Collapsed;
        CenterPanel.Content=null;
        foreach(var b in _centerNavigation)System.Windows.Controls.Primitives.Selector.SetIsSelected(b,(string)b.Tag==page);
        if(module!=null){PageTitle.Text=module.Title;PageSubtitle.Text=module.Description;CenterPanel.Content=new CenterRecordsView(App.Database,page,_currentUser);}
        if(page=="Centro"){PageTitle.Text="Centro y cuentas";PageSubtitle.Text="Nombre del centro y creación de cuentas independientes.";CenterPanel.Content=BuildCenterSettings();}
        if(page=="Inventario"){PageTitle.Text="Alimentos y limpieza";PageSubtitle.Text="Inventario, entradas, salidas y existencias mínimas.";CenterPanel.Content=new PantryView(App.Database,_currentUser);}
    }
    private void EditPatient_Click(object sender,RoutedEventArgs e)
    {
        if(PatientsGrid.SelectedItem is not Patient original){StatusText.Text="Selecciona el paciente que quieres modificar.";return;}
        var patient=JsonSerializer.Deserialize<Patient>(JsonSerializer.Serialize(original))!;
        var form=new CenterForm("Editar paciente",$"{original.Folio} · Sus bitácoras se conservan. Al elegir Baja o Egresado se libera su cama. Los cambios quedan registrados.",v=>
        {
            patient.Folio=v["folio"];patient.FirstName=v["first"];patient.LastName=v["last"];patient.LastName2=v["last2"];patient.AdmissionDate=v["admission"];patient.Status=v["status"];patient.TrafficLight=v["traffic"];patient.WeeklyFee=CenterForm.Number(v["fee"]);patient.Guardian=v["guardian"];patient.Phone=v["phone"];patient.Notes=v["notes"];
            App.Database.UpdatePatient(patient,original,_currentUser);
        }){Owner=this};
        form.Add(new("folio","Folio",true),patient.Folio);form.Add(new("first","Nombre(s)",true),patient.FirstName);form.Add(new("last","Apellido paterno"),patient.LastName);form.Add(new("last2","Apellido materno"),patient.LastName2);form.Add(new("admission","Fecha de ingreso",true,"date"),patient.AdmissionDate);form.Add(new("status","Estado",true,"choice",["Activo","Egresado","Baja"]),patient.Status);form.Add(new("traffic","Semáforo",true,"choice",["Verde","Amarillo","Rojo"]),patient.TrafficLight);form.Add(new("fee","Cuota semanal (MXN; punto decimal)",true),patient.WeeklyFee.ToString(CultureInfo.InvariantCulture));form.Add(new("guardian","Tutor / responsable"),patient.Guardian);form.Add(new("phone","Teléfono"),patient.Phone);form.Add(new("notes","Notas",false,"memo"),patient.Notes);
        if(form.ShowDialog()==true){RefreshAll();StatusText.Text=$"Paciente actualizado: {patient.FullName}.";}
    }
    private void WhatsAppPatient_Click(object sender,RoutedEventArgs e)
    {
        if(PatientsGrid.SelectedItem is not Patient patient){StatusText.Text="Selecciona el paciente cuyo contacto quieres abrir en WhatsApp.";return;}
        if(string.IsNullOrWhiteSpace(patient.Phone)){StatusText.Text="Este paciente no tiene un teléfono de contacto. Puedes agregarlo con Editar seleccionado.";return;}
        try
        {
            var recipient=string.IsNullOrWhiteSpace(patient.Guardian)?"":$" {patient.Guardian.Trim()}";
            var message=$"Hola{recipient}, le escribimos de {App.Database.GetCenterName()} en relación con {patient.FirstName.Trim()}.";
            WhatsAppService.OpenChat(patient.Phone,message);
            StatusText.Text="WhatsApp abierto. Revisa el destinatario y el mensaje antes de enviarlo.";
        }
        catch(Exception ex){StatusText.Text="No fue posible abrir WhatsApp: "+ex.Message;}
    }
    private void Patient_DoubleClick(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        if(PatientsGrid.SelectedItem is Patient)EditPatient_Click(sender,e);
    }
    private void Profile_Click(object sender,RoutedEventArgs e)
    {
        var current=App.Database.GetProfileName(_currentEmail);
        var form=new CenterForm("Mi perfil","Cambia el nombre que aparece en la plataforma. Tu correo de acceso se conserva.",v=>App.Database.UpdateProfileName(_currentEmail,v["name"],current)){Owner=this,Height=430};
        form.Add(new("name","Nombre para mostrar",true),current);
        if(form.ShowDialog()==true){_currentUser=App.Database.GetProfileName(_currentEmail);SignedUser.Text=_currentUser;WelcomeTitle.Text=$"Bienvenido, {_currentUser.Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}.";ShowPanel("Inicio");StatusText.Text="Nombre de usuario actualizado.";}
    }
}
