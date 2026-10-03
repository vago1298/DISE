using System.Text.Json;
using CadLink.Cad;
using CadLink.Cad.PlanoEstructural;

int fallos = 0;

void Vale(string que, bool ok)
{
    Console.WriteLine((ok ? "  OK    " : "  FALLA ") + que);
    if (!ok) { fallos++; }
}

var e = EstiloDibujo.PorDefecto();
EstiloDibujo.Actual = e;

Console.WriteLine("\n---------- Los valores por defecto son los de siempre ----------");
var sec = e.Perfil(EstiloDibujo.Secciones);
Vale("secciones: Bahnschrift SemiLight", sec.Texto("fuente") == "BAHNSCHRIFT SEMILIGHT");
Vale("secciones: numero de cota 0.017 y marca 0.02 _OPEN90",
    sec.Numero("cota.alto") == 0.017 && sec.Numero("cota.marca.tam") == 0.02 && sec.Texto("cota.marca") == "_OPEN90");
Vale("secciones: cotas en 253 con el numero en 1",
    sec.ColorAci("cota.color.lineas") == 253 && sec.ColorAci("cota.color.texto") == 1 && sec.ColorAci("capa.COTAS") == 253);

var zap = e.Perfil(EstiloDibujo.Zapatas);
Vale("zapatas: COTA_ESTRUCTURAL de 0.025 y Arial", zap.Numero("cota.alto") == 0.025 && zap.Texto("fuente") == "Arial");
Vale("zapatas: titulo 0.07 y rotulos 0.015", zap.Numero("alto.titulo") == 0.07 && zap.Numero("alto.rotulos") == 0.015);
Vale("zapatas: COTAS sin color propio (0)", zap.ColorAci("capa.COTAS") == 0);

var mur = e.Perfil(EstiloDibujo.Muros);
Vale("muros: COTA_MC de 0.08, llamadas de 0.09", mur.Numero("cota.alto") == 0.08 && mur.Numero("alto.rotulos") == 0.09);

var pla = e.Perfil(EstiloDibujo.PlacaBase);
Vale("placa: COTA_ACERO oblicua, textos de 0.016",
    pla.Texto("cota.marca") == "_OBLIQUE" && pla.Numero("alto.texto") == 0.016 && pla.ColorAci("capa.PLACA BASE") == 140);

Vale("comun: VAR_#5 en 160 y VAR_#6 en 4, como la macro",
    e.ColorDeVarilla("#5") == 160 && e.ColorDeVarilla("#6") == 4 && CapasCad.ColorDeCapa("VAR_#5") == 160);
Vale("una capa que no es de la macro sigue sin color", CapasCad.ColorDeCapa("OTRA") == CapasCad.SinColor);

// Los de la planta son PARAMETROS de su hoja CONFIG: sus defectos tienen que ser los de la hoja.
var hoja = ConfigPlano.PorOmision.ToDictionary(r => r.Parametro, r => r.Valor);
var distintos = e.Perfil(EstiloDibujo.PlantaEtabs).Ajustes
    .Where(a => !hoja.TryGetValue(a.Clave, out var v) || v != a.Defecto)
    .Select(a => a.Clave).ToList();
Vale("planta: cada ajuste es un parametro de CONFIG con su mismo defecto " + string.Join(", ", distintos),
    distintos.Count == 0);

Vale("sin tocar nada, no hay nada que guardar", e.ParaGuardar().Count == 0);
Vale("y cada valor por defecto es valido", e.Perfiles.SelectMany(p => p.Ajustes).All(a => a.Problema(a.Defecto).Length == 0));

Console.WriteLine("\n---------- Cambiar, guardar y leer ----------");
var c = e.Copia();
c.Perfil(EstiloDibujo.Muros).Buscar("cota.alto")!.Valor = "0,12";
c.Perfil(EstiloDibujo.Comun).Buscar("capa.VAR_#5")!.Valor = "30";
c.Perfil(EstiloDibujo.Secciones).Buscar("cota.marca")!.Valor = "_ARCHTICK";
Vale("la copia no toca el actual", e.Perfil(EstiloDibujo.Muros).Numero("cota.alto") == 0.08);
Vale("con coma tambien se lee", c.Perfil(EstiloDibujo.Muros).Numero("cota.alto") == 0.12);

var json = JsonSerializer.Serialize(c.ParaGuardar());
var leido = EstiloDibujo.PorDefecto();
leido.Aplicar(JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json));
Vale("se guardan solo los 3 cambios", c.ParaGuardar().Sum(p => p.Value.Count) == 3);
Vale("y al leerlos vuelven", leido.Perfil(EstiloDibujo.Muros).Numero("cota.alto") == 0.12
    && leido.ColorDeVarilla("#5") == 30 && leido.Perfil(EstiloDibujo.Secciones).Texto("cota.marca") == "_ARCHTICK");

EstiloDibujo.Actual = leido;
Vale("los dibujantes ven el color nuevo de la capa", CapasCad.ColorDeCapa("VAR_#5") == 30);
EstiloDibujo.Actual = EstiloDibujo.PorDefecto();

var roto = EstiloDibujo.PorDefecto();
var puestos = roto.Aplicar(new Dictionary<string, Dictionary<string, string>>
{
    ["muros"] = new() { ["cota.alto"] = "-3", ["no.existe"] = "1" },
    ["comun"] = new() { ["capa.VAR_#5"] = "900" },
    ["perfil-viejo"] = new() { ["x"] = "1" },
    ["secciones"] = new() { ["cota.marca"] = "_INVENTADA", ["fuente"] = "Arial" },
});
Vale("lo que no sirve se ignora y queda el defecto", puestos == 1
    && roto.Perfil(EstiloDibujo.Muros).Numero("cota.alto") == 0.08 && roto.ColorDeVarilla("#5") == 160
    && roto.Perfil(EstiloDibujo.Secciones).Texto("cota.marca") == "_OPEN90"
    && roto.Perfil(EstiloDibujo.Secciones).Texto("fuente") == "Arial");

var r = EstiloDibujo.PorDefecto().Perfil(EstiloDibujo.Zapatas);
r.Buscar("alto.titulo")!.Valor = "0.2";
r.Restaurar();
Vale("restaurar vuelve al defecto", r.Numero("alto.titulo") == 0.07 && !r.Cambiado("alto.titulo"));
var igual = EstiloDibujo.PorDefecto().Perfil(EstiloDibujo.Zapatas);
igual.Buscar("alto.titulo")!.Valor = "0.070";
Vale("0.070 es lo mismo que 0.07: no cuenta como cambio", !igual.Cambiado("alto.titulo"));

Console.WriteLine("\n---------- Los colores de la paleta ----------");
Vale("1 rojo, 5 azul, 7 blanco", EstiloDibujo.Rgb(1) == (255, 0, 0) && EstiloDibujo.Rgb(5) == (0, 0, 255) && EstiloDibujo.Rgb(7) == (255, 255, 255));
Vale("160 es azul (0, 64, 255)", EstiloDibujo.Rgb(160) == (0, 64, 255));
Vale("10 es rojo y 11 rojo palido", EstiloDibujo.Rgb(10) == (255, 0, 0) && EstiloDibujo.Rgb(11) == (255, 128, 128));
Vale("140 es azul cielo (0, 191, 255)", EstiloDibujo.Rgb(140) == (0, 191, 255));

Console.WriteLine(fallos == 0 ? "\n TODO CORRECTO" : $"\n {fallos} FALLO(S)");
return fallos == 0 ? 0 : 1;
