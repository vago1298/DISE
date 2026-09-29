using System.Globalization;
using System.Text;

namespace CadLink.Ifc;

/// <summary>Una referencia a otra entidad, que se escribe <c>#123</c>.</summary>
/// <remarks>
/// Es un tipo aparte y no un <see cref="int"/> a proposito. En STEP el numero 123 y la
/// referencia #123 se escriben distinto, y son los dos igual de frecuentes en la misma
/// llamada: <c>IfcExtrudedAreaSolid(#perfil, #sitio, #eje, 3.5)</c>. Con un int para las
/// dos cosas, confundirlas no da ningun error de compilacion y produce un archivo que
/// abre mal sin decir por que.
/// </remarks>
public readonly record struct Ref(int Id);

/// <summary>Un valor de enumeracion de STEP, que se escribe entre puntos: <c>.ELEMENT.</c></summary>
public readonly record struct Enumeracion(string Nombre);

/// <summary>Un atributo <b>derivado</b>, que en STEP se escribe con un asterisco.</summary>
/// <remarks>
/// No es lo mismo que <c>$</c>. El dolar dice "este atributo opcional viene vacio"; el
/// asterisco dice "este atributo lo calcula el esquema, no se da". Donde el esquema pide
/// derivado, un <c>$</c> es invalido. Sale en <c>IfcSIUnit.Dimensions</c> y en los cuatro
/// primeros de <c>IfcGeometricRepresentationSubContext</c>.
/// </remarks>
public readonly record struct Derivado;

/// <summary>Una LISTA de valores, que en STEP va entre parentesis.</summary>
/// <remarks>
/// <para>
/// Existe para evitar una trampa de C# que no da ningun error al compilar. Con
/// <c>Ent(string tipo, params object?[] args)</c>, escribir
/// <c>Ent("IFCCARTESIANPOINT", new object?[] { x, y, z })</c> NO pasa un argumento que es
/// una lista de tres: pasa TRES argumentos, porque un <c>object?[]</c> suelto se enlaza
/// directamente con el <c>params</c>. El resultado es
/// <c>IFCCARTESIANPOINT(0.,0.,0.)</c> -tres atributos- cuando la entidad tiene UNO que es
/// una lista: <c>IFCCARTESIANPOINT((0.,0.,0.))</c>.
/// </para>
/// <para>
/// El archivo sale invalido y nada lo delata: compila, se escribe, y el visor lo abre
/// vacio. Con este tipo la intencion queda dicha y el error es imposible.
/// Se construye con <see cref="EscritorPaso.L"/>.
/// </para>
/// </remarks>
public readonly record struct Lista(object?[] Items);

/// <summary>Texto que se escribe tal cual, sin comillas ni escapes.</summary>
/// <remarks>
/// Para los valores TIPADOS de IFC, que son una llamada dentro del argumento:
/// <c>IFCLABEL('C 40x40')</c>, <c>IFCLENGTHMEASURE(0.4)</c>. No hay forma de expresarlos
/// con los demas casos, porque no son ni cadena ni real ni referencia.
/// Se usa solo desde <see cref="ExportadorIfc"/>, que arma el texto con los ayudantes de
/// esta misma clase, asi que no queda nada sin escapar.
/// </remarks>
public readonly record struct Crudo(string Texto);

/// <summary>
/// Escribe un archivo en el formato de texto de STEP (ISO 10303-21), que es el que usa un
/// <c>.ifc</c>.
/// </summary>
/// <remarks>
/// Deliberadamente tonto: no sabe nada de IFC, solo de como se escribe una entidad y como
/// se escapa un valor. Todo lo que sabe de IFC esta en <see cref="ExportadorIfc"/>.
/// </remarks>
public sealed class EscritorPaso
{
    private readonly StringBuilder _cuerpo = new();
    private int _ultimo;

    /// <summary>Cuantas entidades se han escrito.</summary>
    public int Entidades => _ultimo;

    /// <summary>Escribe una entidad y devuelve la referencia con que citarla.</summary>
    public Ref Ent(string tipo, params object?[] args)
    {
        var id = ++_ultimo;

        _cuerpo.Append('#').Append(id.ToString(CultureInfo.InvariantCulture))
               .Append('=').Append(tipo).Append('(');

        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0)
            {
                _cuerpo.Append(',');
            }

            Valor(_cuerpo, args[i]);
        }

        _cuerpo.Append(");\n");

        return new Ref(id);
    }

    /// <summary>El cuerpo, o sea lo que va entre <c>DATA;</c> y <c>ENDSEC;</c>.</summary>
    public string Cuerpo => _cuerpo.ToString();

    /// <summary>Arma una <see cref="Lista"/>. Lease "lista de".</summary>
    /// <remarks>
    /// SIEMPRE se usa esto para un atributo que es una lista, incluso de un solo elemento.
    /// El porque esta en <see cref="Lista"/>.
    /// </remarks>
    public static Lista L(params object?[] items) => new(items);

    // ==================================================================
    //  Formato de cada tipo de valor
    // ==================================================================

    private static void Valor(StringBuilder sb, object? v)
    {
        switch (v)
        {
            case null:
                // El dolar es "sin valor". NO es lo mismo que la cadena vacia: varios
                // atributos de IFC son opcionales y un '' donde tocaba $ hace que un
                // visor estricto rechace el archivo.
                sb.Append('$');
                return;

            case Ref r:
                sb.Append('#').Append(r.Id.ToString(CultureInfo.InvariantCulture));
                return;

            case Enumeracion e:
                sb.Append('.').Append(e.Nombre).Append('.');
                return;

            case Derivado:
                sb.Append('*');
                return;

            case Crudo c:
                sb.Append(c.Texto);
                return;

            case Lista l:
                sb.Append('(');

                for (var i = 0; i < l.Items.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    Valor(sb, l.Items[i]);
                }

                sb.Append(')');
                return;

            case bool b:
                sb.Append(b ? ".T." : ".F.");
                return;

            case string s:
                Cadena(sb, s);
                return;

            case int i:
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                return;

            case long l:
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                return;

            case double d:
                sb.Append(Real(d));
                return;

            case IEnumerable<object?> lista:
                sb.Append('(');
                var primero = true;

                foreach (var x in lista)
                {
                    if (!primero)
                    {
                        sb.Append(',');
                    }

                    Valor(sb, x);
                    primero = false;
                }

                sb.Append(')');
                return;

            default:
                throw new NotSupportedException(
                    $"No se sabe escribir un valor de tipo {v.GetType().Name} en STEP.");
        }
    }

    /// <summary>Un real de STEP.</summary>
    /// <remarks>
    /// SIEMPRE lleva punto decimal, incluso cuando el valor es entero: en STEP <c>3</c> es
    /// un entero y <c>3.</c> es un real, y son tipos distintos. Un 3 donde el esquema pide
    /// un real es un archivo invalido, y el sintoma tipico es que el visor abre el modelo
    /// vacio sin dar ningun error.
    /// Y en invariante, claro: con la coma decimal de es-MX, "3,5" partiria la entidad en
    /// dos argumentos.
    /// </remarks>
    public static string Real(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            throw new ArgumentException(
                "Un real de STEP no puede ser NaN ni infinito; llego " + d, nameof(d));
        }

        // El cero negativo se normaliza: "-0." es valido pero confunde al comparar
        // archivos, y no aporta nada.
        if (d == 0.0)
        {
            return "0.";
        }

        var t = d.ToString("0.0###############", CultureInfo.InvariantCulture);

        // "0.0###" deja "3.0" para el entero 3; se recorta a "3." que es la forma corta
        // y la que escriben los demas exportadores.
        if (t.EndsWith(".0", StringComparison.Ordinal))
        {
            t = t[..^1];
        }

        return t;
    }

    /// <summary>Una cadena de STEP, entre apostrofos y con lo que no es ASCII escapado.</summary>
    /// <remarks>
    /// <para>
    /// Aqui esta el detalle que rompe los nombres en espanol. Una cadena de STEP admite
    /// ASCII y nada mas; los demas caracteres van en una secuencia <c>\X2\</c> con cada
    /// unidad UTF-16 en cuatro digitos hexadecimales, cerrada con <c>\X0\</c>. Escribir la
    /// Ñ en UTF-8 dentro del archivo no da error al exportar: da "CAÃ‘ÓN" en Revit.
    /// </para>
    /// <para>
    /// Y el apostrofo se duplica, porque es el propio delimitador. Sin eso, una seccion
    /// llamada <c>VIGA 12"</c> no rompe nada, pero una nota con un apostrofo parte la
    /// entidad por la mitad.
    /// </para>
    /// </remarks>
    public static void Cadena(StringBuilder sb, string s)
    {
        sb.Append('\'');

        var i = 0;

        while (i < s.Length)
        {
            var c = s[i];

            if (c == '\'')
            {
                sb.Append("''");
                i++;
            }
            else if (c == '\\')
            {
                // La barra invertida arranca las secuencias de escape, asi que se dobla.
                sb.Append("\\\\");
                i++;
            }
            else if (c >= ' ' && c <= '~')
            {
                sb.Append(c);
                i++;
            }
            else
            {
                // Se agrupan TODOS los caracteres raros seguidos en una sola secuencia
                // \X2\...\X0\, en vez de abrir y cerrar una por cada letra. "ÑÁ" da
                // \X2\00D100C1\X0\ y no \X2\00D1\X0\\X2\00C1\X0\.
                sb.Append("\\X2\\");

                while (i < s.Length && (s[i] < ' ' || s[i] > '~'))
                {
                    sb.Append(((int)s[i]).ToString("X4", CultureInfo.InvariantCulture));
                    i++;
                }

                sb.Append("\\X0\\");
            }
        }

        sb.Append('\'');
    }

    /// <summary>La misma cadena de STEP, ya envuelta, para poder probarla suelta.</summary>
    public static string Cadena(string s)
    {
        var sb = new StringBuilder();
        Cadena(sb, s);

        return sb.ToString();
    }
}
