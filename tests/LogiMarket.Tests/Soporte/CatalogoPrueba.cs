using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;

namespace LogiMarket.Tests.Soporte;

/// Catálogo oficial de la guía en memoria (pruebas unitarias de dominio).
public sealed class CatalogoPrueba
{
    public CatalogoPrueba()
    {
        P01 = new ProductoSimple("P01", "Mouse USB", "Periféricos", "", 20.00m, 0.20m, 8);
        P02 = new ProductoSimple("P02", "Teclado", "Periféricos", "", 30.00m, 0.70m, 5);
        P03 = new ProductoSimple("P03", "Memoria USB", "Almacenamiento", "", 10.00m, 0.10m, 20);
        P04 = new ProductoSimple("P04", "Webcam", "Video", "", 40.00m, 0.35m, 4);
        P05 = new ProductoSimple("P05", "Alfombrilla", "Accesorios", "", 5.00m, 0.10m, 30);

        KitOficina = new KitCompuesto("KIT_OFICINA", "Kit Oficina", "");
        KitOficina.AgregarComponente(P01, 1);
        KitOficina.AgregarComponente(P02, 1);

        KitTeletrabajo = new KitCompuesto("KIT_TELETRABAJO", "Kit Teletrabajo", "");
        KitTeletrabajo.AgregarComponente(KitOficina, 1);
        KitTeletrabajo.AgregarComponente(P03, 1);

        Ana = new Cliente("0100000001", "Ana López", "ana@example.com", "099", CategoriaCliente.VIP);
        Luis = new Cliente("0100000002", "Luis Ruiz", "luis@example.com", "099", CategoriaCliente.NORMAL);
        Carmen = new Cliente("0100000003", "Carmen Vega", "carmen@example.com", "099", CategoriaCliente.NORMAL);
    }

    public ProductoSimple P01 { get; }
    public ProductoSimple P02 { get; }
    public ProductoSimple P03 { get; }
    public ProductoSimple P04 { get; }
    public ProductoSimple P05 { get; }
    public KitCompuesto KitOficina { get; }
    public KitCompuesto KitTeletrabajo { get; }
    public Cliente Ana { get; }
    public Cliente Luis { get; }
    public Cliente Carmen { get; }
}
