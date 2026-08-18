using System.Security.Cryptography;

namespace CadLink.Licensing;

/// <summary>
/// Llave pública RSA con la que se verifican los tokens de licencia.
/// </summary>
/// <remarks>
/// <para>
/// El paso 1 de la instalación inserta aquí tu llave automáticamente
/// (<c>tools/embed_public_key.py</c>). No hace falta editar este archivo a mano.
/// </para>
/// <para>
/// Aquí solo va la llave PÚBLICA. Que un atacante la lea no le sirve de nada: con
/// la pública se verifica una firma, pero no se puede producir. La llave privada
/// jamás debe salir del servidor.
/// </para>
/// </remarks>
internal static class EmbeddedPublicKey
{
    // Llave publica insertada automaticamente por tools/embed_public_key.py
    public const string Pem = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAnIFnlLqbxMhz+pC7bjWI
        Eiyw/9Sr6yp/4L5nbzh3A0j6MuNGP+ISkBpcjnFc43u3a5na7Q/eFLVhkhNl/VAU
        T/r6WLKBK2tjo/umszn0STTeIYPCRg7+191fQCytW0bDJA52ukALGhj9G2uhCXZR
        2JmqcriJmHHtcYTWIsBeYj8Jqzj+a2BZQJB4TPjnpRwpSMCOczY6KVLDaIdzlCN9
        GuSauggAPlrs/zf1IohtQLlAHp0NN2eHCfHF9ZtZ1C/L96J0FImAZopO26dos0wA
        Xr2nKMPhG5zkZCTnhiU7C7vJcBH0RawFzEtoR4FsIh30f5vVQkbE6smOBYoSLPox
        sQIDAQAB
        -----END PUBLIC KEY-----
        """;

    private static bool _validated;

    /// <summary>
    /// Comprueba que la llave embebida sea una llave RSA usable.
    /// </summary>
    /// <remarks>
    /// <b>Se valida cargando la llave, no buscando un texto.</b> La primera versión
    /// de esta comprobación buscaba la cadena del marcador de posición, y eso estaba
    /// mal por una razón que costó ver: la cadena aparecía también en el propio
    /// código de la comprobación, así que cualquier búsqueda de texto la encontraba
    /// siempre, incluso después de haber insertado la llave correcta.
    ///
    /// Intentar importarla no tiene esa ambigüedad: o es una llave válida, o no lo es.
    /// </remarks>
    public static void EnsureConfigured()
    {
        if (_validated)
        {
            return;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(Pem);

            if (rsa.KeySize < 2048)
            {
                throw new InvalidOperationException(
                    $"La llave de licenciamiento es de {rsa.KeySize} bits. " +
                    "Se requieren al menos 2048. Vuelve a generarla.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new InvalidOperationException(
                "La llave pública de licenciamiento no está configurada.\n\n" +
                "Ejecuta 1-instalar-servidor.bat: ese paso la inserta " +
                "automáticamente a partir de server/keys/public.pem.");
        }

        _validated = true;
    }
}
