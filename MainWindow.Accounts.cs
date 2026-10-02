using System.Windows;
using System.Windows.Controls;

namespace SIRASD.Desktop;

public partial class MainWindow
{
    private void ClearSessionViews()
    {
        _allPatients.Clear();Patients.Clear();Beds.Clear();PatientSearch.Clear();CenterPanel.Content=null;PaymentsPanel.SetDatabase(App.Database);
        ActivePatientsText.Text="0";OccupiedBedsText.Text="0";AvailableBedsText.Text="0";IncidentsText.Text="0";
        OccupancyBar.Value=0;OccupancyText.Text="0%";CapacityText.Text="";SignedUser.Text="";WelcomeTitle.Text="Bienvenido";StatusText.Text="";
    }
    private void ResetCenterBranding()
    {
        Title="SIRASD";SidebarCenterName.Text="Tu centro";SidebarCenterName.ToolTip=null;SidebarMonogram.Text="SIR";CenterBreadcrumb.Text="MI CENTRO / SIRASD";
    }
    private void ApplyCenterBranding()
    {
        var name=App.Database.GetCenterName();Title="SIRASD · "+name;SidebarCenterName.Text=name;SidebarCenterName.ToolTip=name;
        CenterBreadcrumb.Text=name+" / SIRASD";
        SidebarMonogram.Text=string.Concat(name.Split(' ',StringSplitOptions.RemoveEmptyEntries).Take(3).Select(s=>char.ToUpperInvariant(s[0])));
    }
    private UIElement BuildCenterSettings()
    {
        var panel=new StackPanel();
        var center=new StackPanel();var heading=CenterUi.Text(App.Database.GetCenterName(),23);heading.FontWeight=FontWeights.SemiBold;center.Children.Add(heading);
        center.Children.Add(CenterUi.Text($"Sesión: {_currentUser}\nCorreo: {_currentEmail}"));
        var receipt=App.Database.GetCenterReceiptInfo();
        center.Children.Add(CenterUi.Text($"Datos para comprobantes\nDomicilio: {(string.IsNullOrWhiteSpace(receipt.Address)?"Sin capturar":receipt.Address)}\nTeléfono: {(string.IsNullOrWhiteSpace(receipt.Phone)?"Sin capturar":receipt.Phone)}\nRFC: {(string.IsNullOrWhiteSpace(receipt.Rfc)?"Sin capturar":receipt.Rfc)}"));
        var centerActions=new WrapPanel();centerActions.Children.Add(CenterUi.Button("Editar nombre del centro",EditCenterName,true));centerActions.Children.Add(CenterUi.Button("Editar datos de comprobantes",EditReceiptInfo));center.Children.Add(centerActions);panel.Children.Add(CenterUi.Frame(center));
        var accounts=new StackPanel();accounts.Children.Add(CenterUi.Text("Usuarios con datos independientes",20));
        accounts.Children.Add(CenterUi.Text("Cada cuenta nueva tendrá su propio centro y comenzará sin pacientes, bitácoras ni inventario. Tendrá 30 camas disponibles y accederá con su propio correo y contraseña."));
        accounts.Children.Add(CenterUi.Button("+ Crear cuenta independiente",()=>CreateAccount_Click(this,new RoutedEventArgs()),true));panel.Children.Add(CenterUi.Frame(accounts));
        panel.Children.Add(CenterUi.Text("Al cerrar sesión y entrar con otro correo se cambia de espacio. Los respaldos contienen los datos del centro de la sesión abierta. Las cuentas creadas en esta computadora no se sincronizan automáticamente con otros dispositivos."));
        return new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private void EditCenterName()
    {
        var form=new CenterForm("Nombre del centro","Este cambio se aplica al centro de la sesión abierta y conserva todos sus registros.",v=>App.Database.UpdateCenterName(v["center"],_currentUser)){Owner=this,Height=470,MinHeight=400};
        form.Add(new("center","Nombre del centro de rehabilitación",true),App.Database.GetCenterName());
        if(form.ShowDialog()==true){ApplyCenterBranding();ShowPanel("Centro");StatusText.Text="Nombre del centro actualizado.";}
    }
    private void EditReceiptInfo()
    {
        var current=App.Database.GetCenterReceiptInfo();
        var form=new CenterForm("Datos para comprobantes","Estos datos aparecerán en los nuevos tickets. Cada pago conserva una copia para que sus reimpresiones no cambien.",v=>App.Database.UpdateCenterReceiptInfo(v["address"],v["phone"],v["rfc"],v["footer"],_currentUser)){Owner=this,Height=690,MinHeight=560};
        form.Add(new("address","Domicilio del centro"),current.Address);form.Add(new("phone","Teléfono(s)"),current.Phone);form.Add(new("rfc","RFC"),current.Rfc);form.Add(new("footer","Mensaje final del comprobante",false,"memo"),current.Footer);
        if(form.ShowDialog()==true){ShowPanel("Centro");StatusText.Text="Datos para comprobantes actualizados.";}
    }
    private void CreateAccount_Click(object sender,RoutedEventArgs e)
    {
        string? createdEmail=null;
        var form=new CenterForm("Crear cuenta independiente","Esta cuenta tendrá sus propios datos. El correo debe ser distinto al de las cuentas existentes.",v=>
        {
            _accounts.CreateIndependentAccount(v["center"],v["user"],v["email"],v["password"],v["confirmation"]);createdEmail=v["email"].Trim();
        }){Owner=this};
        form.Add(new("center","Nombre del centro",true));form.Add(new("user","Nombre del usuario",true));form.Add(new("email","Correo para iniciar sesión",true));
        form.Add(new("password","Contraseña (de 8 a 128 caracteres)",true,"password"));form.Add(new("confirmation","Confirmar contraseña",true,"password"));
        if(form.ShowDialog()!=true)return;
        if(ApplicationView.Visibility==Visibility.Visible)
        {
            StatusText.Text=$"Cuenta creada: {createdEmail}. Cierra sesión para ingresar a su espacio.";
            MessageBox.Show($"La cuenta {createdEmail} está lista.\n\nCierra sesión e ingresa con su correo y contraseña para usar sus datos independientes.","Cuenta creada",MessageBoxButton.OK,MessageBoxImage.Information);
        }
        else{EmailBox.Text=createdEmail;PasswordBox.Clear();LoginError.Text="Cuenta creada. Ingresa con tu nueva contraseña.";}
    }
}
