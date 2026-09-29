namespace CadLink.Ifc;

/// <summary>
/// El <c>GlobalId</c> que lleva toda entidad de IFC: un UUID de 128 bits escrito en
/// 22 caracteres.
/// </summary>
/// <remarks>
/// <para>
/// No es base64 del de toda la vida. IFC usa su propio alfabeto de 64 simbolos
/// -digitos, letras, y luego <c>_</c> y <c>$</c>- y una particion muy concreta de los
/// 128 bits: el byte alto de <c>Data1</c> en <b>2</b> caracteres, y luego cinco grupos
/// de <b>24 bits en 4 caracteres</b> cada uno. Suman 8 + 120 = 128 bits en 2 + 20 = 22
/// caracteres.
/// </para>
/// <para>
/// Se implementa la particion del estandar, y no un base64 cualquiera de los 16 bytes,
/// para que el identificador se pueda DESCOMPRIMIR de vuelta al UUID original con
/// cualquier otra herramienta. Un base64 propio daria una cadena igual de unica y que
/// Revit aceptaria sin chistar, pero romperia esa equivalencia sin avisar.
/// <see cref="ADesde"/> existe precisamente para poder comprobar el ida y vuelta.
/// </para>
/// </remarks>
public static class IfcGuid
{
    /// <summary>El alfabeto de IFC, en orden. El valor de cada simbolo es su posicion.</summary>
    /// <remarks>
    /// El orden importa y no es el del base64 comun: aqui van primero los digitos, luego
    /// las mayusculas, luego las minusculas, y al final el guion bajo y el signo de pesos.
    /// </remarks>
    public const string Alfabeto =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    /// <summary>Cuantos caracteres tiene un GlobalId. El esquema lo fija en 22.</summary>
    public const int Largo = 22;

    /// <summary>Comprime un <see cref="Guid"/> en su GlobalId de 22 caracteres.</summary>
    public static string De(Guid guid)
    {
        // ToByteArray() entrega Data1, Data2 y Data3 en el orden de la maquina, que en
        // Intel y ARM es el de byte menos significativo primero. Se reconstruyen con
        // BitConverter para no depender de eso.
        var b = guid.ToByteArray();

        var data1 = BitConverter.ToUInt32(b, 0);
        var data2 = BitConverter.ToUInt16(b, 4);
        var data3 = BitConverter.ToUInt16(b, 6);
        // b[8..15] son los ocho bytes de Data4, que ya van en orden.

        var trozos = new uint[6];

        trozos[0] = data1 >> 24;                       // 8 bits  -> 2 caracteres
        trozos[1] = data1 & 0x00FFFFFFu;               // 24 bits -> 4 caracteres
        trozos[2] = ((uint)data2 << 8) | (uint)(data3 >> 8);
        trozos[3] = ((uint)(data3 & 0x00FFu) << 16) | ((uint)b[8] << 8) | b[9];
        trozos[4] = ((uint)b[10] << 16) | ((uint)b[11] << 8) | b[12];
        trozos[5] = ((uint)b[13] << 16) | ((uint)b[14] << 8) | b[15];

        var sb = new System.Text.StringBuilder(Largo);

        Escribir(sb, trozos[0], 2);

        for (var i = 1; i < 6; i++)
        {
            Escribir(sb, trozos[i], 4);
        }

        return sb.ToString();
    }

    /// <summary>Descomprime un GlobalId de 22 caracteres en el <see cref="Guid"/> original.</summary>
    /// <remarks>
    /// Existe para poder PROBAR <see cref="De"/>: un comprimir que se equivoque en el
    /// reparto de bits casi nunca da una cadena de aspecto raro, asi que mirarla no dice
    /// nada. El ida y vuelta si.
    /// </remarks>
    public static Guid ADesde(string global)
    {
        if (global is null)
        {
            throw new ArgumentNullException(nameof(global));
        }

        if (global.Length != Largo)
        {
            throw new ArgumentException(
                $"Un GlobalId de IFC mide {Largo} caracteres, y este mide {global.Length}.",
                nameof(global));
        }

        var trozos = new uint[6];

        trozos[0] = Leer(global, 0, 2);

        for (var i = 1; i < 6; i++)
        {
            trozos[i] = Leer(global, 2 + ((i - 1) * 4), 4);
        }

        var data1 = (trozos[0] << 24) | trozos[1];
        var data2 = (ushort)(trozos[2] >> 8);
        var data3 = (ushort)(((trozos[2] & 0xFFu) << 8) | (trozos[3] >> 16));

        var data4 = new byte[8];
        data4[0] = (byte)((trozos[3] >> 8) & 0xFFu);
        data4[1] = (byte)(trozos[3] & 0xFFu);
        data4[2] = (byte)((trozos[4] >> 16) & 0xFFu);
        data4[3] = (byte)((trozos[4] >> 8) & 0xFFu);
        data4[4] = (byte)(trozos[4] & 0xFFu);
        data4[5] = (byte)((trozos[5] >> 16) & 0xFFu);
        data4[6] = (byte)((trozos[5] >> 8) & 0xFFu);
        data4[7] = (byte)(trozos[5] & 0xFFu);

        return new Guid((int)data1, (short)data2, (short)data3, data4);
    }

    /// <summary>Un GlobalId nuevo, al azar.</summary>
    public static string Nuevo() => De(Guid.NewGuid());

    /// <summary>
    /// Un GlobalId <b>estable</b>: la misma clave da siempre el mismo identificador.
    /// </summary>
    /// <remarks>
    /// Es lo que se usa para exportar. Con identificadores al azar, volver a exportar el
    /// mismo modelo produce un archivo donde Revit no reconoce NADA de lo que ya habia
    /// importado, y en vez de actualizar duplica todo. Con la clave derivada del nombre
    /// del elemento, la segunda exportacion trae los mismos GlobalId y las herramientas
    /// que comparan por identificador pueden emparejar.
    /// </remarks>
    public static string Estable(string clave)
    {
        // MD5 da justo los 16 bytes que mide un UUID. Aqui no se usa como funcion de
        // seguridad -no hay nada que proteger en el nombre de una trabe-, solo para
        // repartir de forma estable, asi que su debilidad criptografica no aplica.
        var datos = System.Text.Encoding.UTF8.GetBytes(clave ?? string.Empty);
        var hash = System.Security.Cryptography.MD5.HashData(datos);

        return De(new Guid(hash));
    }

    private static void Escribir(System.Text.StringBuilder sb, uint valor, int cuantos)
    {
        // Se emite del caracter mas significativo al menos, que es como se lee.
        for (var i = cuantos - 1; i >= 0; i--)
        {
            sb.Append(Alfabeto[(int)((valor >> (6 * i)) & 0x3Fu)]);
        }
    }

    private static uint Leer(string texto, int desde, int cuantos)
    {
        var valor = 0u;

        for (var i = 0; i < cuantos; i++)
        {
            var pos = Alfabeto.IndexOf(texto[desde + i]);

            if (pos < 0)
            {
                throw new ArgumentException(
                    $"El caracter '{texto[desde + i]}' no pertenece al alfabeto de IFC.",
                    nameof(texto));
            }

            valor = (valor << 6) | (uint)pos;
        }

        return valor;
    }
}
