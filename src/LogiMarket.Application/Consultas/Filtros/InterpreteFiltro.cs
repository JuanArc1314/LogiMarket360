using System.Globalization;
using System.Text;
using LogiMarket.Application.Comun;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Consultas.Filtros;

public sealed class ErrorSintaxisFiltro : ExcepcionAplicacion
{
    public ErrorSintaxisFiltro(string mensaje, int posicion)
        : base("FILTRO_INVALIDO", $"Error de sintaxis en la posición {posicion + 1}: {mensaje}")
    {
        Posicion = posicion;
    }

    public int Posicion { get; }
}

/// Expresión abstracta del intérprete.
public interface IExpresionPedido
{
    bool Interpretar(PedidoResumenDto pedido);
}

// ---------------- Expresiones terminales ----------------

public sealed class ExpresionEstado(EstadoPedido estado) : IExpresionPedido
{
    public bool Interpretar(PedidoResumenDto pedido) => pedido.Estado == estado;
    public override string ToString() => $"ESTADO = {estado}";
}

public sealed class ExpresionTotal(string operador, decimal valor) : IExpresionPedido
{
    public bool Interpretar(PedidoResumenDto pedido) => operador switch
    {
        "=" => pedido.Total == valor,
        ">=" => pedido.Total >= valor,
        "<=" => pedido.Total <= valor,
        ">" => pedido.Total > valor,
        "<" => pedido.Total < valor,
        _ => throw new InvalidOperationException($"Operador no soportado: {operador}")
    };

    public override string ToString() => $"TOTAL {operador} {valor.ToString(CultureInfo.InvariantCulture)}";
}

public sealed class ExpresionCliente(string texto) : IExpresionPedido
{
    public bool Interpretar(PedidoResumenDto pedido) =>
        pedido.Cliente.Contains(texto, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"CLIENTE = \"{texto}\"";
}

// ---------------- Expresiones no terminales ----------------

public sealed class ExpresionY(IExpresionPedido izquierda, IExpresionPedido derecha) : IExpresionPedido
{
    public bool Interpretar(PedidoResumenDto pedido) => izquierda.Interpretar(pedido) && derecha.Interpretar(pedido);
    public override string ToString() => $"({izquierda} AND {derecha})";
}

public sealed class ExpresionO(IExpresionPedido izquierda, IExpresionPedido derecha) : IExpresionPedido
{
    public bool Interpretar(PedidoResumenDto pedido) => izquierda.Interpretar(pedido) || derecha.Interpretar(pedido);
    public override string ToString() => $"({izquierda} OR {derecha})";
}

// ---------------- Análisis léxico ----------------

internal enum TipoToken { Palabra, Numero, Texto, Operador, AbreParentesis, CierraParentesis, Fin }

internal sealed record Token(TipoToken Tipo, string Valor, int Posicion)
{
    public bool EsPalabra(string palabra) =>
        Tipo == TipoToken.Palabra && string.Equals(Valor, palabra, StringComparison.OrdinalIgnoreCase);
}

internal static class AnalizadorLexico
{
    public static List<Token> Tokenizar(string entrada)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < entrada.Length)
        {
            var c = entrada[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { tokens.Add(new Token(TipoToken.AbreParentesis, "(", i++)); continue; }
            if (c == ')') { tokens.Add(new Token(TipoToken.CierraParentesis, ")", i++)); continue; }
            if (c is '>' or '<' or '=')
            {
                var inicio = i++;
                var op = c.ToString();
                if (c != '=' && i < entrada.Length && entrada[i] == '=') { op += "="; i++; }
                tokens.Add(new Token(TipoToken.Operador, op, inicio));
                continue;
            }
            if (c is '"' or '\'')
            {
                var inicio = i++;
                var sb = new StringBuilder();
                while (i < entrada.Length && entrada[i] != c) sb.Append(entrada[i++]);
                if (i >= entrada.Length) throw new ErrorSintaxisFiltro("texto sin comilla de cierre", inicio);
                i++;
                tokens.Add(new Token(TipoToken.Texto, sb.ToString(), inicio));
                continue;
            }
            if (char.IsDigit(c))
            {
                var inicio = i;
                while (i < entrada.Length && (char.IsDigit(entrada[i]) || entrada[i] == '.')) i++;
                tokens.Add(new Token(TipoToken.Numero, entrada[inicio..i], inicio));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var inicio = i;
                while (i < entrada.Length && (char.IsLetterOrDigit(entrada[i]) || entrada[i] is '_' or '-' or '.')) i++;
                tokens.Add(new Token(TipoToken.Palabra, entrada[inicio..i], inicio));
                continue;
            }
            throw new ErrorSintaxisFiltro($"carácter no reconocido '{c}'", i);
        }
        tokens.Add(new Token(TipoToken.Fin, "", entrada.Length));
        return tokens;
    }
}

// ---------------- Análisis sintáctico (descendente recursivo) ----------------

public sealed class ParserFiltroPedidos
{
    private List<Token> _tokens = [];
    private int _pos;

    public IExpresionPedido Analizar(string entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada))
            throw new ErrorSintaxisFiltro("el filtro está vacío", 0);
        _tokens = AnalizadorLexico.Tokenizar(entrada);
        _pos = 0;
        var expresion = Expresion();
        if (Actual.Tipo != TipoToken.Fin)
            throw new ErrorSintaxisFiltro($"se esperaba AND, OR o fin, pero se encontró '{Actual.Valor}'", Actual.Posicion);
        return expresion;
    }

    private Token Actual => _tokens[_pos];
    private Token Consumir() => _tokens[_pos++];

    private IExpresionPedido Expresion()
    {
        var izquierda = Termino();
        while (Actual.EsPalabra("OR"))
        {
            Consumir();
            izquierda = new ExpresionO(izquierda, Termino());
        }
        return izquierda;
    }

    private IExpresionPedido Termino()
    {
        var izquierda = Factor();
        while (Actual.EsPalabra("AND"))
        {
            Consumir();
            izquierda = new ExpresionY(izquierda, Factor());
        }
        return izquierda;
    }

    private IExpresionPedido Factor()
    {
        if (Actual.Tipo == TipoToken.AbreParentesis)
        {
            var abre = Consumir();
            var interna = Expresion();
            if (Actual.Tipo != TipoToken.CierraParentesis)
                throw new ErrorSintaxisFiltro($"falta ')' para el paréntesis abierto en {abre.Posicion + 1}", Actual.Posicion);
            Consumir();
            return interna;
        }
        return Comparacion();
    }

    private IExpresionPedido Comparacion()
    {
        var campo = Consumir();
        if (campo.Tipo != TipoToken.Palabra)
            throw new ErrorSintaxisFiltro($"se esperaba ESTADO, TOTAL o CLIENTE y se encontró '{campo.Valor}'", campo.Posicion);
        var operador = Consumir();
        if (operador.Tipo != TipoToken.Operador)
            throw new ErrorSintaxisFiltro($"se esperaba un operador después de {campo.Valor}", operador.Posicion);

        switch (campo.Valor.ToUpperInvariant())
        {
            case "ESTADO":
            {
                ExigirIgual(operador, "ESTADO");
                var valor = Consumir();
                if (valor.Tipo is not (TipoToken.Palabra or TipoToken.Texto) ||
                    !Enum.TryParse<EstadoPedido>(valor.Valor, true, out var estado) || int.TryParse(valor.Valor, out _))
                    throw new ErrorSintaxisFiltro($"estado desconocido '{valor.Valor}'", valor.Posicion);
                return new ExpresionEstado(estado);
            }
            case "TOTAL":
            {
                var valor = Consumir();
                if (valor.Tipo != TipoToken.Numero ||
                    !decimal.TryParse(valor.Valor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numero))
                    throw new ErrorSintaxisFiltro($"TOTAL requiere un número y se encontró '{valor.Valor}'", valor.Posicion);
                return new ExpresionTotal(operador.Valor, numero);
            }
            case "CLIENTE":
            {
                ExigirIgual(operador, "CLIENTE");
                if (Actual.Tipo == TipoToken.Texto) return new ExpresionCliente(Consumir().Valor);
                var palabras = new List<string>();
                while (Actual.Tipo is TipoToken.Palabra or TipoToken.Numero && !Actual.EsPalabra("AND") && !Actual.EsPalabra("OR"))
                    palabras.Add(Consumir().Valor);
                if (palabras.Count == 0)
                    throw new ErrorSintaxisFiltro("CLIENTE requiere un texto", Actual.Posicion);
                return new ExpresionCliente(string.Join(' ', palabras));
            }
            default:
                throw new ErrorSintaxisFiltro($"campo desconocido '{campo.Valor}' (use ESTADO, TOTAL o CLIENTE)", campo.Posicion);
        }
    }

    private static void ExigirIgual(Token operador, string campo)
    {
        if (operador.Valor != "=")
            throw new ErrorSintaxisFiltro($"{campo} solo admite el operador '='", operador.Posicion);
    }
}
