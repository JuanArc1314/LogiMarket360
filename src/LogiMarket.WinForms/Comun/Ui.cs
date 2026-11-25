using System.Globalization;
using LogiMarket.Application.Comun;
using LogiMarket.Domain.Comun;

namespace LogiMarket.WinForms.Comun;

//paleta y tipografia comunes (coherentes con los wireframes de referencia)
public static class Estilos
{
    public static readonly Color Fondo = Color.FromArgb(248, 250, 252);
    public static readonly Color Lateral = Color.FromArgb(241, 245, 249);
    public static readonly Color Borde = Color.FromArgb(203, 213, 225);
    public static readonly Color Texto = Color.FromArgb(15, 23, 42);
    public static readonly Color TextoSuave = Color.FromArgb(100, 116, 139);
    public static readonly Color Primario = Color.FromArgb(51, 65, 85);
    public static readonly Color Exito = Color.FromArgb(22, 101, 52);
    public static readonly Color Error = Color.FromArgb(185, 28, 28);
    public static readonly Color Aviso = Color.FromArgb(180, 83, 9);

    public static readonly Font Normal = new("Segoe UI", 9.75f);
    public static readonly Font Negrita = new("Segoe UI", 9.75f, FontStyle.Bold);
    public static readonly Font Titulo = new("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Font Subtitulo = new("Segoe UI", 10f);
    public static readonly Font Kpi = new("Segoe UI", 18f, FontStyle.Bold);
    public static readonly Font Mono = new("Consolas", 10f);
}

//fabrica de controles reutilizables (DRY entre pantallas)
public static class UiKit
{
    public static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

    public static Button Boton(string texto, EventHandler alPulsar, bool primario = false, int ancho = 0)
    {
        var boton = new Button
        {
            Text = texto,
            AutoSize = ancho == 0,
            Width = ancho == 0 ? 75 : ancho,
            Height = 32,
            Margin = new Padding(0, 4, 8, 4),
            Padding = new Padding(8, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primario ? Estilos.Primario : Color.White,
            ForeColor = primario ? Color.White : Estilos.Texto,
            Font = primario ? Estilos.Negrita : Estilos.Normal,
            Cursor = Cursors.Hand,
            AccessibleName = texto.Replace("&", "")
        };
        boton.FlatAppearance.BorderColor = Estilos.Borde;
        boton.Click += alPulsar;
        return boton;
    }

    public static Label Etiqueta(string texto, bool negrita = false) => new()
    {
        Text = texto,
        AutoSize = true,
        Font = negrita ? Estilos.Negrita : Estilos.Normal,
        ForeColor = negrita ? Estilos.Texto : Estilos.TextoSuave,
        Margin = new Padding(0, 8, 6, 2),
        Anchor = AnchorStyles.Left
    };

    public static TextBox Texto(string nombreAccesible, int ancho = 200) => new()
    {
        Width = ancho,
        Font = Estilos.Normal,
        AccessibleName = nombreAccesible,
        Margin = new Padding(0, 4, 12, 4)
    };

    public static NumericUpDown Numero(string nombreAccesible, decimal minimo, decimal maximo, int decimales = 0, int ancho = 90) => new()
    {
        Minimum = minimo,
        Maximum = maximo,
        DecimalPlaces = decimales,
        Increment = decimales == 0 ? 1 : 0.01m,
        Width = ancho,
        Font = Estilos.Normal,
        AccessibleName = nombreAccesible,
        Margin = new Padding(0, 4, 12, 4),
        ThousandsSeparator = false
    };

    public static ComboBox Combo(string nombreAccesible, int ancho = 200) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FormattingEnabled = true, //necesario para que el evento format personalice el texto
        Width = ancho,
        Font = Estilos.Normal,
        AccessibleName = nombreAccesible,
        Margin = new Padding(0, 4, 12, 4)
    };

    public static FlowLayoutPanel Fila(params Control[] controles)
    {
        var fila = new FilaFlujo
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(0, 2, 0, 2)
        };
        fila.Controls.AddRange(controles);
        return fila;
    }

    //fila que calcula su alto con el ancho real del contenedor
    //sin esto, al estrechar la ventana los controles que saltan a una segunda linea quedaban
    //debajo de la grilla vecina: se veian a medias y no recibian el clic del raton
    private sealed class FilaFlujo : FlowLayoutPanel
    {
        public override Size GetPreferredSize(Size proposedSize)
        {
            if (Dock == DockStyle.Top && Parent is not null)
                proposedSize = new Size(Math.Max(1, Parent.DisplayRectangle.Width - Margin.Horizontal), 0);
            return base.GetPreferredSize(proposedSize);
        }
    }

    //etiqueta + control (etiqueta asociada para accesibilidad y validacion cercana al campo)
    public static FlowLayoutPanel Campo(string etiqueta, Control control)
    {
        control.AccessibleName ??= etiqueta;
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 0, 10, 0) };
        panel.Controls.Add(new Label { Text = etiqueta, AutoSize = true, Font = Estilos.Negrita, ForeColor = Estilos.TextoSuave, Margin = new Padding(0, 4, 0, 0) });
        panel.Controls.Add(control);
        return panel;
    }

    public static GroupBox Grupo(string titulo, Control contenido, int alto = 0)
    {
        var grupo = new GroupBox
        {
            Text = titulo,
            Font = Estilos.Negrita,
            ForeColor = Estilos.Texto,
            Dock = alto == 0 ? DockStyle.Fill : DockStyle.Top,
            Height = alto == 0 ? 100 : alto,
            Padding = new Padding(8)
        };
        contenido.Dock = DockStyle.Fill;
        contenido.Font = Estilos.Normal;
        grupo.Controls.Add(contenido);
        return grupo;
    }

    public static DataGridView Grid(string nombreAccesible)
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            EnableHeadersVisualStyles = false,
            Font = Estilos.Normal,
            AccessibleName = nombreAccesible,
            StandardTab = true //TAB sale de la grilla hacia el siguiente control
        };
        grid.ColumnHeadersDefaultCellStyle.Font = Estilos.Negrita;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Estilos.Lateral;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = Estilos.Texto;
        grid.DefaultCellStyle.FormatProvider = Cultura;
        return grid;
    }

    //agrega una columna enlazada
    //formato "M" = moneda con 2 decimales
    public static DataGridView Columna(this DataGridView grid, string propiedad, string titulo, int peso = 100, string? formato = null)
    {
        var columna = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propiedad,
            HeaderText = titulo,
            Name = propiedad,
            FillWeight = peso,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        if (formato == "M")
        {
            columna.DefaultCellStyle.Format = "0.00";
            columna.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        else if (formato is not null)
            columna.DefaultCellStyle.Format = formato;
        columna.DefaultCellStyle.FormatProvider = Cultura;
        grid.Columns.Add(columna);
        return grid;
    }

    //enlaza datos conservando la fila seleccionada (seleccion persistente tras refrescar)
    public static void Enlazar<T>(this DataGridView grid, IReadOnlyList<T> datos, Func<T, object>? clave = null)
    {
        object? seleccionada = clave is not null && grid.CurrentRow?.DataBoundItem is T actual ? clave(actual) : null;
        grid.DataSource = datos.ToList();
        if (seleccionada is null || clave is null) return;
        foreach (DataGridViewRow fila in grid.Rows)
        {
            if (fila.DataBoundItem is T item && Equals(clave(item), seleccionada))
            {
                grid.CurrentCell = fila.Cells.Cast<DataGridViewCell>().FirstOrDefault(c => c.Visible);
                fila.Selected = true;
                break;
            }
        }
    }

    public static T? Seleccion<T>(this DataGridView grid) where T : class => grid.CurrentRow?.DataBoundItem as T;

    public static string Dinero(decimal valor) => Domain.Comun.Dinero.Formatear(valor);

    public static bool Confirmar(IWin32Window? dueno, string mensaje, string titulo = "Confirmar operación") =>
        MessageBox.Show(dueno, mensaje, titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    //traduce excepciones de negocio a mensajes claros
    //las inesperadas se informan como error
    public static string Describir(Exception ex) => ex switch
    {
        ExcepcionDominio d => $"{d.Message}\n(código: {d.Codigo})",
        ExcepcionAplicacion a => $"{a.Message}\n(código: {a.Codigo})",
        _ => $"Error inesperado: {ex.Message}"
    };
}
