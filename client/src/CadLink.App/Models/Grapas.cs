using CadLink.Cad;

namespace CadLink.App.Models;

/// <summary>
/// Una <b>grapa</b>: el estribo suplementario que une dos varillas longitudinales,
/// con un gancho en cada punta.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza que en obra se llama grapa y en los planos aparece como estribo
/// suplementario o <i>crosstie</i>: una varilla recta con un doblez en cada extremo
/// que se agarra de dos varillas del armado para arriostrarlas.
/// </para>
/// <para>
/// El diámetro se guarda como la <b>clave</b> de la varilla —<c>#3</c>, <c>#4</c>…— y
/// no en centímetros, igual que todos los demás diámetros de la hoja, para que haya
/// una sola tabla de conversión: <see cref="Varilla.TryDiametroCm"/>. Al pasar al
/// dibujante se resuelve a centímetros en <c>AFormatoCad</c>, porque el motor de dibujo
/// no puede recibir un diámetro sin reconocer.
/// </para>
/// <para>
/// Las dos varillas se señalan con <see cref="RefVarilla"/>, que vive en la capa de
/// CAD: es la misma señal que usa el dibujante de AutoCAD para encontrarlas en el
/// plano, y tiene que haber una sola definición o las grapas del plano no coincidirían
/// con las de la pantalla.
/// </para>
/// </remarks>
public sealed class GrapaSeccion
{
    /// <summary>Una de las dos varillas que la grapa agarra.</summary>
    public RefVarilla A { get; init; }

    /// <summary>La otra.</summary>
    public RefVarilla B { get; init; }

    /// <summary>Clave de la varilla con la que se arma la grapa.</summary>
    public string Diametro { get; set; } = "#3";

    /// <summary>
    /// ¿Esta grapa une justo esas dos varillas, en cualquier orden?
    /// </summary>
    /// <remarks>
    /// Sin orden a propósito: una grapa entre la varilla 1 y la 5 es la MISMA que
    /// entre la 5 y la 1. Es lo que permite que volver a marcar el mismo par la
    /// quite, en vez de acabar con dos grapas encimadas que se ven como una sola y
    /// no hay manera de borrar.
    /// </remarks>
    public bool Une(RefVarilla x, RefVarilla y) =>
        (A.Equals(x) && B.Equals(y)) || (A.Equals(y) && B.Equals(x));
}
