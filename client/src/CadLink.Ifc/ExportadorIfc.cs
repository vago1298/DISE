using System.Globalization;

namespace CadLink.Ifc;

/// <summary>
/// Escribe el modelo como un archivo <c>.ifc</c>, para abrirlo o vincularlo en Revit.
/// </summary>
/// <remarks>
/// <para>
/// Se emite <b>IFC4</b> y no IFC2X3 por una razon concreta: el juego
/// <c>IfcMaterialProfileSet</c> / <c>IfcMaterialProfile</c>, con el que una seccion viaja
/// como perfil con nombre en vez de como un solido anonimo, es de IFC4. Y es justo lo que
/// hace falta para poder emparejar cada seccion con una familia en Revit.
/// </para>
/// <para>
/// El orden de los atributos de cada entidad se tomo del esquema EXPRESS oficial
/// (IFC4_ADD2_TC1.exp de buildingSMART), y va anotado con su cuenta en cada llamada:
/// <c>IfcColumn [9]</c> quiere decir nueve atributos. <c>tools/verificar_ifc.py</c>
/// comprueba esas cuentas sobre el archivo generado, que es lo que impide que un atributo
/// de mas o de menos pase inadvertido: un IFC con la cuenta mal no da ningun error al
/// escribirse y Revit lo abre vacio.
/// </para>
/// </remarks>
public static class ExportadorIfc
{
    /// <summary>El esquema que se declara en la cabecera.</summary>
    public const string Esquema = "IFC4";

    /// <summary>Como se identifica el programa dentro del archivo.</summary>
    public const string Programa = "CadLink";

    /// <summary>El nombre del juego de propiedades donde viaja la seccion.</summary>
    /// <remarks>
    /// No se llama <c>Pset_...</c> a proposito: ese prefijo esta reservado para los juegos
    /// definidos por buildingSMART, y usarlo para uno propio es incorrecto aunque funcione.
    /// </remarks>
    public const string JuegoDePropiedades = "CadLink_Seccion";

    /// <summary>Escribe el archivo y devuelve lo que se exporto.</summary>
    public static ResumenIfc AArchivo(ModeloIfc modelo, string ruta, DateTimeOffset? cuando = null)
    {
        if (modelo is null)
        {
            throw new ArgumentNullException(nameof(modelo));
        }

        if (string.IsNullOrWhiteSpace(ruta))
        {
            throw new ArgumentException("Falta la ruta del archivo.", nameof(ruta));
        }

        var (texto, resumen) = Generar(modelo, Path.GetFileName(ruta), cuando);

        // Sin BOM: tras el escapado \X2\ el contenido es ASCII puro, y un BOM delante de
        // "ISO-10303-21;" hace que algunos lectores no reconozcan el archivo.
        File.WriteAllText(ruta, texto, new System.Text.UTF8Encoding(false));

        return resumen;
    }

    /// <summary>Genera el texto del archivo, sin escribir nada.</summary>
    /// <remarks>
    /// Separado de <see cref="AArchivo"/> para poder probarlo sin tocar el disco, y para
    /// que las pruebas puedan fijar la fecha y comparar dos corridas.
    /// </remarks>
    public static (string Texto, ResumenIfc Resumen) Generar(
        ModeloIfc modelo, string? nombreArchivo = null, DateTimeOffset? cuando = null)
    {
        if (modelo is null)
        {
            throw new ArgumentNullException(nameof(modelo));
        }

        var fecha = cuando ?? DateTimeOffset.UtcNow;
        var r = new ResumenIfc();
        var p = new EscritorPaso();

        // ============================================================
        //  Quien y cuando
        // ============================================================

        // IfcPerson [8]: Identification, FamilyName, GivenName, MiddleNames,
        //   PrefixTitles, SuffixTitles, Roles, Addresses
        var persona = p.Ent("IFCPERSON", null, Programa, null, null, null, null, null, null);

        // IfcOrganization [5]: Identification, Name, Description, Roles, Addresses
        var organizacion = p.Ent("IFCORGANIZATION", null, Programa, null, null, null);

        // IfcPersonAndOrganization [3]: ThePerson, TheOrganization, Roles
        var quien = p.Ent("IFCPERSONANDORGANIZATION", persona, organizacion, null);

        // IfcApplication [4]: ApplicationDeveloper, Version, ApplicationFullName,
        //   ApplicationIdentifier
        var app = p.Ent("IFCAPPLICATION", organizacion, "1.0", Programa, Programa);

        // IfcOwnerHistory [8]: OwningUser, OwningApplication, State, ChangeAction,
        //   LastModifiedDate, LastModifyingUser, LastModifyingApplication, CreationDate
        var dueno = p.Ent("IFCOWNERHISTORY",
            quien, app, null, new Enumeracion("ADDED"), null, null, null,
            (int)fecha.ToUnixTimeSeconds());

        // ============================================================
        //  Unidades: metros, que es en lo que viene todo el modelo
        // ============================================================

        // IfcSIUnit [4]: Dimensions, UnitType, Prefix, Name. Dimensions es DERIVADO.
        var metro = p.Ent("IFCSIUNIT",
            default(Derivado), new Enumeracion("LENGTHUNIT"), null, new Enumeracion("METRE"));
        var metro2 = p.Ent("IFCSIUNIT",
            default(Derivado), new Enumeracion("AREAUNIT"), null, new Enumeracion("SQUARE_METRE"));
        var metro3 = p.Ent("IFCSIUNIT",
            default(Derivado), new Enumeracion("VOLUMEUNIT"), null, new Enumeracion("CUBIC_METRE"));
        var radian = p.Ent("IFCSIUNIT",
            default(Derivado), new Enumeracion("PLANEANGLEUNIT"), null, new Enumeracion("RADIAN"));

        // IfcUnitAssignment [1]: Units
        var unidades = p.Ent("IFCUNITASSIGNMENT",
            EscritorPaso.L(metro, metro2, metro3, radian));

        // ============================================================
        //  Contexto geometrico
        // ============================================================
        var origen3d = Punto(p, 0, 0, 0);
        var ejeZ = Direccion(p, 0, 0, 1);
        var ejeX = Direccion(p, 1, 0, 0);

        // IfcAxis2Placement3D [3]: Location, Axis, RefDirection
        var colocacionMundo = p.Ent("IFCAXIS2PLACEMENT3D", origen3d, null, null);

        // IfcGeometricRepresentationContext [6]: ContextIdentifier, ContextType,
        //   CoordinateSpaceDimension, Precision, WorldCoordinateSystem, TrueNorth
        var contexto = p.Ent("IFCGEOMETRICREPRESENTATIONCONTEXT",
            null, "Model", 3, 1e-5, colocacionMundo, null);

        // IfcGeometricRepresentationSubContext [10]: ContextIdentifier, ContextType,
        //   CoordinateSpaceDimension, Precision, WorldCoordinateSystem, TrueNorth,
        //   ParentContext, TargetScale, TargetView, UserDefinedTargetView
        // Los cuatro del medio son DERIVADOS: los hereda del contexto padre.
        var subcontexto = p.Ent("IFCGEOMETRICREPRESENTATIONSUBCONTEXT",
            "Body", "Model",
            default(Derivado), default(Derivado), default(Derivado), default(Derivado),
            contexto, null, new Enumeracion("MODEL_VIEW"), null);

        // El 2D compartido por todos los perfiles parametricos, centrado en el origen.
        var origen2d = p.Ent("IFCCARTESIANPOINT", EscritorPaso.L(0.0, 0.0));
        var pos2d = p.Ent("IFCAXIS2PLACEMENT2D", origen2d, null);

        // ============================================================
        //  Proyecto, terreno, edificio y niveles
        // ============================================================
        var obra = string.IsNullOrWhiteSpace(modelo.Obra) ? "Modelo estructural" : modelo.Obra.Trim();

        // IfcProject [9]: GlobalId, OwnerHistory, Name, Description, ObjectType,
        //   LongName, Phase, RepresentationContexts, UnitsInContext
        var proyecto = p.Ent("IFCPROJECT",
            IfcGuid.Estable("proyecto|" + obra), dueno, obra,
            string.IsNullOrWhiteSpace(modelo.Programa) ? null : "Exportado de " + modelo.Programa,
            null, null, null, EscritorPaso.L(contexto), unidades);

        var colocacionSitio = p.Ent("IFCLOCALPLACEMENT", null, colocacionMundo);

        // IfcSite [14]: GlobalId, OwnerHistory, Name, Description, ObjectType,
        //   ObjectPlacement, Representation, LongName, CompositionType,
        //   RefLatitude, RefLongitude, RefElevation, LandTitleNumber, SiteAddress
        var sitio = p.Ent("IFCSITE",
            IfcGuid.Estable("sitio|" + obra), dueno, "Terreno", null, null,
            colocacionSitio, null, null, new Enumeracion("ELEMENT"),
            null, null, null, null, null);

        var colocacionEdificio = p.Ent("IFCLOCALPLACEMENT", colocacionSitio, colocacionMundo);

        // IfcBuilding [12]: GlobalId, OwnerHistory, Name, Description, ObjectType,
        //   ObjectPlacement, Representation, LongName, CompositionType,
        //   ElevationOfRefHeight, ElevationOfTerrain, BuildingAddress
        var edificio = p.Ent("IFCBUILDING",
            IfcGuid.Estable("edificio|" + obra), dueno, obra, null, null,
            colocacionEdificio, null, null, new Enumeracion("ELEMENT"), null, null, null);

        var niveles = Niveles(p, modelo, dueno, colocacionEdificio, r);

        // La jerarquia espacial, que hay que decir con relaciones aparte: en IFC el que un
        // nivel este "dentro" del edificio no se deduce de nada, se declara.
        //
        // Sin estas tres relaciones el archivo es sintacticamente correcto y contiene todo
        // -proyecto, terreno, edificio, niveles y piezas-, pero desconectado: al importarlo
        // no hay estructura espacial de la que colgar nada, y lo que aparece en Revit es un
        // monton de geometria sin niveles. No lo avisa nadie.
        //
        // IfcRelAggregates [6]: GlobalId, OwnerHistory, Name, Description,
        //   RelatingObject, RelatedObjects
        p.Ent("IFCRELAGGREGATES",
            IfcGuid.Estable("agrega|proyecto|" + obra), dueno, null, null,
            proyecto, EscritorPaso.L(sitio));

        p.Ent("IFCRELAGGREGATES",
            IfcGuid.Estable("agrega|sitio|" + obra), dueno, null, null,
            sitio, EscritorPaso.L(edificio));

        if (niveles.Count > 0)
        {
            p.Ent("IFCRELAGGREGATES",
                IfcGuid.Estable("agrega|edificio|" + obra), dueno, null, null,
                edificio,
                EscritorPaso.L(niveles.Values
                    .OrderBy(n => n.ElevacionM)
                    .Select(n => (object?)n.Entidad)
                    .ToArray()));
        }

        // ============================================================
        //  Secciones: un perfil y un tipo por cada una
        // ============================================================
        var secciones = new Dictionary<string, Seccion>(StringComparer.Ordinal);
        var materiales = new Dictionary<string, Ref>(StringComparer.OrdinalIgnoreCase);

        // ============================================================
        //  Las piezas
        // ============================================================
        var porNivel = new Dictionary<string, List<Ref>>(StringComparer.Ordinal);
        var porTipo = new Dictionary<string, List<Ref>>(StringComparer.Ordinal);

        foreach (var barra in modelo.Barras)
        {
            var hecha = Barra(p, barra, niveles, secciones, materiales, subcontexto,
                pos2d, dueno, ejeZ, ejeX, r);

            if (hecha is null)
            {
                continue;
            }

            Apuntar(porNivel, hecha.Value.Nivel, hecha.Value.Elemento);
            Apuntar(porTipo, hecha.Value.Tipo, hecha.Value.Elemento);

            switch (barra.Clase)
            {
                case ClaseIfc.Columna: r.Columnas++; break;
                case ClaseIfc.Diagonal: r.Diagonales++; break;
                default: r.Trabes++; break;
            }
        }

        foreach (var pano in modelo.Panos)
        {
            var hecha = Pano(p, pano, niveles, secciones, materiales, subcontexto, dueno, r);

            if (hecha is null)
            {
                continue;
            }

            Apuntar(porNivel, hecha.Value.Nivel, hecha.Value.Elemento);
            Apuntar(porTipo, hecha.Value.Tipo, hecha.Value.Elemento);

            if (pano.Clase == ClaseIfc.Muro)
            {
                r.Muros++;
            }
            else
            {
                r.Losas++;
            }
        }

        // ============================================================
        //  Relaciones
        // ============================================================

        // Cada pieza colgada de su nivel. Sin esto Revit importa la geometria pero no la
        // asocia a ningun nivel, y aparece todo en el nivel base.
        foreach (var (nombre, lista) in porNivel)
        {
            if (lista.Count == 0 || !niveles.TryGetValue(nombre, out var nivel))
            {
                continue;
            }

            // IfcRelContainedInSpatialStructure [6]: GlobalId, OwnerHistory, Name,
            //   Description, RelatedElements, RelatingStructure
            p.Ent("IFCRELCONTAINEDINSPATIALSTRUCTURE",
                IfcGuid.Estable("enNivel|" + nombre), dueno, null, null,
                EscritorPaso.L(lista.Cast<object?>().ToArray()), nivel.Entidad);
        }

        // Cada pieza con su TIPO. Esto es lo que hace que Revit cree un tipo por seccion
        // en vez de un elemento generico por barra, y lo que permite luego mapear cada
        // tipo a una familia.
        foreach (var (clave, lista) in porTipo)
        {
            if (lista.Count == 0 || !secciones.TryGetValue(clave, out var s))
            {
                continue;
            }

            // IfcRelDefinesByType [6]: GlobalId, OwnerHistory, Name, Description,
            //   RelatedObjects, RelatingType
            p.Ent("IFCRELDEFINESBYTYPE",
                IfcGuid.Estable("deTipo|" + clave), dueno, null, null,
                EscritorPaso.L(lista.Cast<object?>().ToArray()), s.Tipo);
        }

        r.Niveles = niveles.Count;
        r.Secciones = secciones.Count;
        r.Entidades = p.Entidades;

        var texto = Cabecera(modelo, nombreArchivo, fecha) + p.Cuerpo + Cierre();

        return (texto, r);
    }

    // ==================================================================
    //  Niveles
    // ==================================================================

    private sealed record Nivel(Ref Entidad, Ref Colocacion, double ElevacionM, string Nombre);

    private static Dictionary<string, Nivel> Niveles(
        EscritorPaso p, ModeloIfc modelo, Ref dueno, Ref colocacionEdificio, ResumenIfc r)
    {
        var salida = new Dictionary<string, Nivel>(StringComparer.Ordinal);

        var lista = modelo.Niveles
            .Where(n => !string.IsNullOrWhiteSpace(n.Nombre))
            .GroupBy(n => n.Nombre.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(n => n.ElevacionM)
            .ToList();

        if (lista.Count == 0)
        {
            // Un IFC sin ningun nivel es valido, pero Revit no tiene donde poner las
            // piezas. Se inventa uno en la cota cero y se avisa.
            lista.Add(new NivelIfc { Nombre = "Nivel 0", ElevacionM = 0 });
            r.Avisos.Add(
                "El modelo no traia niveles; se creo uno solo, «Nivel 0», en la cota cero.");
        }

        foreach (var n in lista)
        {
            var nombre = n.Nombre.Trim();

            // El nivel se coloca a SU elevacion, y las piezas se colocaran relativas a el.
            // La otra opcion -dejar los niveles en cero y usar coordenadas absolutas- es
            // igual de valida y mas facil, pero entonces la cota del nivel en Revit y la
            // altura real de las piezas salen de sitios distintos, y en cuanto una no
            // cuadra con la otra no hay forma de saber cual esta mal.
            var punto = Punto(p, 0, 0, n.ElevacionM);
            var eje = p.Ent("IFCAXIS2PLACEMENT3D", punto, null, null);
            var colocacion = p.Ent("IFCLOCALPLACEMENT", colocacionEdificio, eje);

            // IfcBuildingStorey [10]: GlobalId, OwnerHistory, Name, Description,
            //   ObjectType, ObjectPlacement, Representation, LongName, CompositionType,
            //   Elevation
            var entidad = p.Ent("IFCBUILDINGSTOREY",
                IfcGuid.Estable("nivel|" + nombre), dueno, nombre, null, null,
                colocacion, null, null, new Enumeracion("ELEMENT"), n.ElevacionM);

            salida[nombre] = new Nivel(entidad, colocacion, n.ElevacionM, nombre);
        }

        return salida;
    }

    /// <summary>El nivel que le toca a una pieza, o el mas cercano por cota.</summary>
    private static Nivel DondeVa(
        Dictionary<string, Nivel> niveles, string pedido, double z, ResumenIfc r)
    {
        var nombre = (pedido ?? string.Empty).Trim();

        if (nombre.Length > 0 && niveles.TryGetValue(nombre, out var exacto))
        {
            return exacto;
        }

        // Se busca sin distinguir mayusculas antes de rendirse: "Story1" y "STORY1" son el
        // mismo nivel y en un modelo heredado aparecen las dos formas.
        foreach (var (k, v) in niveles)
        {
            if (string.Equals(k, nombre, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        // No cuadra ningun nombre: se cuelga del nivel cuya cota este mas cerca. Es mejor
        // que descartar la pieza, y el aviso dice lo que paso.
        var cerca = niveles.Values.OrderBy(n => Math.Abs(n.ElevacionM - z)).First();

        if (nombre.Length > 0)
        {
            r.Avisos.Add(
                $"El nivel «{nombre}» no estaba en la lista de niveles; "
                + $"lo que iba en el se colgo de «{cerca.Nombre}», que es el mas cercano.");
        }

        return cerca;
    }

    // ==================================================================
    //  Una barra
    // ==================================================================

    private static (Ref Elemento, string Nivel, string Tipo)? Barra(
        EscritorPaso p, BarraIfc b,
        Dictionary<string, Nivel> niveles,
        Dictionary<string, Seccion> secciones,
        Dictionary<string, Ref> materiales,
        Ref subcontexto, Ref pos2d, Ref dueno, Ref ejeZ, Ref ejeX, ResumenIfc r)
    {
        var largo = b.LargoM;

        if (largo < 1e-6)
        {
            r.Avisos.Add(
                $"La barra «{b.Etiqueta}» tiene los dos extremos en el mismo punto; no se exporto.");

            return null;
        }

        var p1 = new[] { b.X1, b.Y1, b.Z1 };
        var p2 = new[] { b.X2, b.Y2, b.Z2 };

        var (e1, e2, e3) = EjesSanos(b, p1, p2, r);

        var nivel = DondeVa(niveles, b.Nivel, Math.Max(b.Z1, b.Z2), r);
        var seccion = Seccion.Obtener(p, b.Clase, b.Seccion, secciones, materiales, pos2d, dueno, r);

        // ---- La colocacion, que es la parte delicada ----
        //
        // Se quiere que el perfil quede con su ANCHO sobre el eje local 3 y su PERALTE
        // sobre el local 2, porque asi lo define el esquema para los perfiles
        // parametricos: OverallWidth va sobre la X del perfil y OverallDepth sobre la Y.
        //
        // Un IfcAxis2Placement3D es DERECHO: dados Axis (su Z) y RefDirection (su X), la Y
        // sale de Z x X. Si se pone Axis = E1 y RefDirection = E3, la Y resulta
        // E1 x E3 = -E2, o sea el perfil queda espejeado sobre el eje 2. En un rectangulo,
        // un circulo o una I eso no se ve. En un angulo, un canal o una te, SI: sale con
        // las piernas al revés.
        //
        // La salida no es girar el perfil -un giro no arregla un espejo- sino extruir al
        // REVES: Axis = -E1, RefDirection = E3. Entonces
        //     Y = Axis x RefDirection = (-E1) x E3 = -(E1 x E3) = E2
        // que es lo que se queria, y sigue siendo una terna derecha.
        //
        // La consecuencia es que la extrusion arranca en el extremo J y avanza hacia el I:
        //     P2 + largo * (-E1) = P2 - (P2 - P1) = P1
        // Por eso el origen es P2 y no P1. Parece del reves y esta bien; se comprueba en
        // tools/prueba-ifc.
        var corrido = Geometria.Sumar(
            p2,
            Geometria.Sumar(
                Geometria.Escalar(e2, b.CorrimientoEje2M),
                Geometria.Escalar(e3, b.CorrimientoEje3M)));

        // Las coordenadas van RELATIVAS al nivel del que cuelga la pieza.
        var local = new[] { corrido[0], corrido[1], corrido[2] - nivel.ElevacionM };

        var lugar = Punto(p, local[0], local[1], local[2]);
        var dirAxis = Direccion(p, -e1[0], -e1[1], -e1[2]);
        var dirRef = Direccion(p, e3[0], e3[1], e3[2]);

        var ejeSolido = p.Ent("IFCAXIS2PLACEMENT3D", lugar, dirAxis, dirRef);

        // IfcExtrudedAreaSolid [4]: SweptArea, Position, ExtrudedDirection, Depth
        // La direccion va en el sistema de la propia colocacion, asi que es su Z local.
        // Una barra siempre sale del catalogo de perfiles, asi que Perfil viene lleno; el
        // Ref? es por los panos, que comparten la clase Seccion pero no tienen perfil.
        var solido = p.Ent("IFCEXTRUDEDAREASOLID",
            seccion.Perfil!.Value, ejeSolido, ejeZ, largo);

        var forma = Forma(p, subcontexto, solido);

        var colocacion = p.Ent("IFCLOCALPLACEMENT", nivel.Colocacion,
            p.Ent("IFCAXIS2PLACEMENT3D", Punto(p, 0, 0, 0), null, null));

        var (entidad, predefinido) = b.Clase switch
        {
            ClaseIfc.Columna => ("IFCCOLUMN", "COLUMN"),
            ClaseIfc.Diagonal => ("IFCMEMBER", "BRACE"),
            _ => ("IFCBEAM", "BEAM")
        };

        var etiqueta = string.IsNullOrWhiteSpace(b.Etiqueta) ? null : b.Etiqueta.Trim();

        // IfcColumn / IfcBeam / IfcMember [9]: GlobalId, OwnerHistory, Name, Description,
        //   ObjectType, ObjectPlacement, Representation, Tag, PredefinedType
        //
        // ObjectType lleva el nombre de la seccion a proposito: el importador de Revit lo
        // usa para nombrar el tipo, asi que es lo que se vera en la lista donde se elige
        // la familia.
        var elemento = p.Ent(entidad,
            IfcGuid.Estable($"barra|{b.Clase}|{b.Etiqueta}|{b.Nivel}"),
            dueno, etiqueta, null, seccion.Nombre,
            colocacion, forma, etiqueta, new Enumeracion(predefinido));

        return (elemento, nivel.Nombre, seccion.Clave);
    }

    /// <summary>
    /// Los ejes locales, comprobados y arreglados si hacia falta.
    /// </summary>
    /// <remarks>
    /// Los ejes llegan calculados por la aplicacion, pero llegan de un modelo que puede
    /// traer cualquier cosa: un marco de largo casi cero, un angulo de giro absurdo, o una
    /// terna que no es ortonormal por acumulacion de redondeos. Si se pasan tal cual, el
    /// archivo sale con piezas torcidas o espejeadas y ABRE sin dar ningun error, que es la
    /// peor forma de fallar. Asi que se comprueban aqui y, si no sirven, se rehacen a
    /// partir de la unica cosa que siempre es fiable: la recta que une los dos extremos.
    /// </remarks>
    private static (double[] E1, double[] E2, double[] E3) EjesSanos(
        BarraIfc b, double[] p1, double[] p2, ResumenIfc r)
    {
        var w = Geometria.Normalizar(Geometria.Restar(p2, p1));

        var e1 = b.E1;
        var e2 = b.E2;
        var e3 = b.E3;

        var sirven = e1 is { Length: 3 } && e2 is { Length: 3 } && e3 is { Length: 3 }
                     && Geometria.EsTernaDerecha(e1, e2, e3);

        // El eje 1 tiene que ir a lo largo de la pieza. Si no, los ejes no son de esta
        // barra y no hay nada que salvar.
        if (sirven && Math.Abs(Geometria.Punto(e1, w)) < 0.999)
        {
            sirven = false;
        }

        if (sirven)
        {
            // Puede venir apuntando de J a I. Se voltea para que siempre vaya de I a J, que
            // es lo que supone el calculo de la colocacion.
            if (Geometria.Punto(e1, w) < 0)
            {
                return (Geometria.Negar(e1), e2, Geometria.Negar(e3));
            }

            return (e1, e2, e3);
        }

        // Reconstruccion: eje 1 a lo largo, eje 2 lo mas vertical que se pueda -que es la
        // convencion de CSI para una trabe- y eje 3 cerrando la terna derecha.
        var z = new double[] { 0, 0, 1 };
        var casiVertical = Math.Abs(Geometria.Punto(w, z)) > 1 - 1e-6;

        // En una pieza vertical no hay "lo mas vertical posible": se toma la X global, que
        // es lo que hace CSI con una columna a cero grados.
        var referencia = casiVertical ? new double[] { 1, 0, 0 } : z;

        var nuevoE3 = Geometria.Normalizar(Geometria.Cruz(referencia, w));
        var nuevoE2 = Geometria.Normalizar(Geometria.Cruz(nuevoE3, w));

        r.Avisos.Add(
            $"Los ejes locales de «{b.Etiqueta}» no formaban una terna valida; "
            + "se reconstruyeron a partir de sus extremos. Revisa su giro si la seccion "
            + "no es simetrica.");

        return (w, nuevoE2, nuevoE3);
    }

    // ==================================================================
    //  Un pano: muro o losa
    // ==================================================================

    private static (Ref Elemento, string Nivel, string Tipo)? Pano(
        EscritorPaso p, PanoIfc pano,
        Dictionary<string, Nivel> niveles,
        Dictionary<string, Seccion> secciones,
        Dictionary<string, Ref> materiales,
        Ref subcontexto, Ref dueno, ResumenIfc r)
    {
        var v = Geometria.SinRepetidos(pano.Vertices);

        if (v.Count < 3)
        {
            r.Avisos.Add(
                $"El pano «{pano.Etiqueta}» tiene menos de tres vertices distintos; no se exporto.");

            return null;
        }

        var (normal, area) = Geometria.NormalDePoligono(v);

        if (area < 1e-9)
        {
            r.Avisos.Add(
                $"El pano «{pano.Etiqueta}» tiene area cero, seguramente por tener todos los "
                + "vertices en linea; no se exporto.");

            return null;
        }

        // En una losa se quiere la normal hacia ARRIBA, para poder colgarla del nivel de
        // forma predecible. Si el contorno viene al reves, se le da la vuelta en vez de
        // voltear la normal: voltear solo la normal deja el perfil espejeado, y un contorno
        // de losa espejeado es una losa distinta.
        if (pano.Clase == ClaseIfc.Losa && normal[2] < 0)
        {
            v.Reverse();
            (normal, area) = Geometria.NormalDePoligono(v);
        }

        var espesor = pano.EspesorM > 1e-6 ? pano.EspesorM : 0.10;

        if (pano.EspesorM <= 1e-6)
        {
            r.Avisos.Add(
                $"El pano «{pano.Etiqueta}» no traia espesor; se exporto con 10 cm.");
        }

        var (ex, ey) = Geometria.PlanoDe(normal);
        var origen = new[] { v[0].X, v[0].Y, v[0].Z };
        var plano = Geometria.Aplanar(v, origen, ex, ey);

        // ex, ey y normal forman terna derecha por construccion, asi que un contorno cuyo
        // giro concuerde con la normal sale antihorario. Se comprueba porque un perfil con
        // el giro al reves es un agujero para algunos lectores.
        if (Geometria.AreaConSigno(plano) < 0)
        {
            plano.Reverse();
        }

        // El contorno de un IfcArbitraryClosedProfileDef tiene que ser una curva CERRADA, y
        // una IfcPolyline se cierra repitiendo su primer punto al final. Los repetidos se
        // quitaron antes justamente para poder anadir aqui uno y solo uno.
        var puntos = new List<object?>(plano.Count + 1);

        foreach (var (u, w2) in plano)
        {
            puntos.Add(p.Ent("IFCCARTESIANPOINT", EscritorPaso.L(u, w2)));
        }

        puntos.Add(puntos[0]);

        var linea = p.Ent("IFCPOLYLINE", EscritorPaso.L(puntos.ToArray()));

        // IfcArbitraryClosedProfileDef [3]: ProfileType, ProfileName, OuterCurve
        var perfil = p.Ent("IFCARBITRARYCLOSEDPROFILEDEF",
            new Enumeracion("AREA"),
            string.IsNullOrWhiteSpace(pano.Seccion) ? null : pano.Seccion.Trim(),
            linea);

        // Donde arranca el espesor:
        //   una LOSA cuelga por debajo del contorno, porque el contorno es su cara
        //   superior, que es la cota con la que se trabaja en los planos;
        //   un MURO se reparte a los dos lados, porque en ETABS el pano es su plano medio.
        var atras = pano.Clase == ClaseIfc.Losa ? espesor : espesor / 2.0;
        var arranque = Geometria.Restar(origen, Geometria.Escalar(normal, atras));

        var nivel = DondeVa(niveles, pano.Nivel, v.Max(q => q.Z), r);

        var lugar = Punto(p, arranque[0], arranque[1], arranque[2] - nivel.ElevacionM);
        var dirAxis = Direccion(p, normal[0], normal[1], normal[2]);
        var dirRef = Direccion(p, ex[0], ex[1], ex[2]);

        var ejeSolido = p.Ent("IFCAXIS2PLACEMENT3D", lugar, dirAxis, dirRef);
        var ejeLocalZ = Direccion(p, 0, 0, 1);

        var solido = p.Ent("IFCEXTRUDEDAREASOLID", perfil, ejeSolido, ejeLocalZ, espesor);
        var forma = Forma(p, subcontexto, solido);

        var colocacion = p.Ent("IFCLOCALPLACEMENT", nivel.Colocacion,
            p.Ent("IFCAXIS2PLACEMENT3D", Punto(p, 0, 0, 0), null, null));

        var esMuro = pano.Clase == ClaseIfc.Muro;
        var etiqueta = string.IsNullOrWhiteSpace(pano.Etiqueta) ? null : pano.Etiqueta.Trim();
        var nombreSeccion = string.IsNullOrWhiteSpace(pano.Seccion) ? null : pano.Seccion.Trim();

        var seccionPano = Seccion.ObtenerPano(p, pano, secciones, materiales, dueno);

        // IfcWall / IfcSlab [9]: GlobalId, OwnerHistory, Name, Description, ObjectType,
        //   ObjectPlacement, Representation, Tag, PredefinedType
        var elemento = p.Ent(esMuro ? "IFCWALL" : "IFCSLAB",
            IfcGuid.Estable($"pano|{pano.Clase}|{pano.Etiqueta}|{pano.Nivel}"),
            dueno, etiqueta, null, nombreSeccion,
            colocacion, forma, etiqueta,
            new Enumeracion(esMuro ? "SHEAR" : "FLOOR"));

        return (elemento, nivel.Nombre, seccionPano.Clave);
    }

    // ==================================================================
    //  Piezas compartidas
    // ==================================================================

    private static Ref Forma(EscritorPaso p, Ref subcontexto, Ref solido)
    {
        // IfcShapeRepresentation [4]: ContextOfItems, RepresentationIdentifier,
        //   RepresentationType, Items
        var rep = p.Ent("IFCSHAPEREPRESENTATION",
            subcontexto, "Body", "SweptSolid", EscritorPaso.L(solido));

        // IfcProductDefinitionShape [3]: Name, Description, Representations
        return p.Ent("IFCPRODUCTDEFINITIONSHAPE", null, null, EscritorPaso.L(rep));
    }

    private static Ref Punto(EscritorPaso p, double x, double y, double z) =>
        p.Ent("IFCCARTESIANPOINT", EscritorPaso.L(x, y, z));

    private static Ref Direccion(EscritorPaso p, double x, double y, double z) =>
        p.Ent("IFCDIRECTION", EscritorPaso.L(x, y, z));

    private static void Apuntar(Dictionary<string, List<Ref>> donde, string clave, Ref que)
    {
        if (!donde.TryGetValue(clave, out var lista))
        {
            lista = new List<Ref>();
            donde[clave] = lista;
        }

        lista.Add(que);
    }

    // ==================================================================
    //  Cabecera y cierre
    // ==================================================================

    private static string Cabecera(ModeloIfc modelo, string? nombreArchivo, DateTimeOffset fecha)
    {
        var sb = new System.Text.StringBuilder();

        sb.Append("ISO-10303-21;\n");
        sb.Append("HEADER;\n");

        // La vista declarada importa: un importador la lee para saber que esperar. Se
        // declara DesignTransferView porque lo que se exporta son solidos por extrusion
        // con perfiles parametricos, que es lo que esa vista permite; ReferenceView seria
        // decir que solo se manda geometria ya triangulada, y no es el caso.
        sb.Append("FILE_DESCRIPTION((")
          .Append(EscritorPaso.Cadena("ViewDefinition [DesignTransferView_V1.0]"))
          .Append("),'2;1');\n");

        sb.Append("FILE_NAME(")
          .Append(EscritorPaso.Cadena(nombreArchivo ?? "modelo.ifc")).Append(',')
          .Append(EscritorPaso.Cadena(fecha.UtcDateTime.ToString(
              "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))).Append(",(")
          .Append(EscritorPaso.Cadena(Programa)).Append("),(")
          .Append(EscritorPaso.Cadena(Programa)).Append("),")
          .Append(EscritorPaso.Cadena(Programa + " " + Esquema)).Append(',')
          .Append(EscritorPaso.Cadena(
              string.IsNullOrWhiteSpace(modelo.Programa)
                  ? Programa
                  : Programa + " desde " + modelo.Programa.Trim())).Append(',')
          .Append(EscritorPaso.Cadena(string.Empty)).Append(");\n");

        sb.Append("FILE_SCHEMA((").Append(EscritorPaso.Cadena(Esquema)).Append("));\n");
        sb.Append("ENDSEC;\n");
        sb.Append("DATA;\n");

        return sb.ToString();
    }

    private static string Cierre() => "ENDSEC;\nEND-ISO-10303-21;\n";

    // ==================================================================
    //  El catalogo de secciones
    // ==================================================================

    private sealed class Seccion
    {
        public required string Clave { get; init; }

        public required string? Nombre { get; init; }

        /// <summary>
        /// El perfil parametrico. Vacio en muros y losas, que llevan un contorno propio por
        /// pieza en vez de un perfil compartido.
        /// </summary>
        public Ref? Perfil { get; init; }

        public required Ref Tipo { get; init; }

        public static Seccion Obtener(
            EscritorPaso p, ClaseIfc clase, SeccionIfc s,
            Dictionary<string, Seccion> cache,
            Dictionary<string, Ref> materiales,
            Ref pos2d, Ref dueno, ResumenIfc r)
        {
            // La clave lleva la CLASE ademas de la seccion: la misma propiedad usada como
            // columna y como trabe necesita dos tipos distintos, porque en IFC un
            // IfcColumnType y un IfcBeamType no son intercambiables.
            var clave = clase + "|" + s.Clave;

            if (cache.TryGetValue(clave, out var ya))
            {
                return ya;
            }

            var perfil = PerfilesIfc.Escribir(p, s, pos2d, r.Avisos);
            var nombre = string.IsNullOrWhiteSpace(s.Nombre) ? null : s.Nombre.Trim();

            var juego = Propiedades(p, s, dueno);

            var (entidadTipo, predefinido) = clase switch
            {
                ClaseIfc.Columna => ("IFCCOLUMNTYPE", "COLUMN"),
                ClaseIfc.Diagonal => ("IFCMEMBERTYPE", "BRACE"),
                _ => ("IFCBEAMTYPE", "BEAM")
            };

            // IfcColumnType / IfcBeamType / IfcMemberType [10]: GlobalId, OwnerHistory,
            //   Name, Description, ApplicableOccurrence, HasPropertySets,
            //   RepresentationMaps, Tag, ElementType, PredefinedType
            //
            // PredefinedType NO es opcional en los tipos, a diferencia de en los elementos:
            // aqui un $ hace invalido el archivo.
            var tipo = p.Ent(entidadTipo,
                IfcGuid.Estable("tipo|" + clave), dueno, nombre, null, null,
                EscritorPaso.L(juego), null, nombre, null, new Enumeracion(predefinido));

            // El material con su perfil, que es lo que hace que la seccion viaje como
            // seccion y no como un solido cualquiera.
            var material = Material(p, s.Material, materiales);

            // IfcMaterialProfile [6]: Name, Description, Material, Profile, Priority, Category
            var matPerfil = p.Ent("IFCMATERIALPROFILE",
                nombre, null, material, perfil, null, null);

            // IfcMaterialProfileSet [4]: Name, Description, MaterialProfiles, CompositeProfile
            var juegoPerfil = p.Ent("IFCMATERIALPROFILESET",
                nombre, null, EscritorPaso.L(matPerfil), null);

            // IfcRelAssociatesMaterial [6]: GlobalId, OwnerHistory, Name, Description,
            //   RelatedObjects, RelatingMaterial
            p.Ent("IFCRELASSOCIATESMATERIAL",
                IfcGuid.Estable("material|" + clave), dueno, null, null,
                EscritorPaso.L(tipo), juegoPerfil);

            var hecha = new Seccion
            {
                Clave = clave,
                Nombre = nombre,
                Perfil = perfil,
                Tipo = tipo
            };

            cache[clave] = hecha;

            return hecha;
        }

        /// <summary>El tipo de un muro o una losa.</summary>
        /// <remarks>
        /// Un pano no tiene perfil parametrico -su geometria es el contorno de cada pieza-
        /// pero SI necesita tipo, y por el mismo motivo que una columna: es lo que hace que
        /// Revit cree un tipo de muro por cada propiedad de area del modelo, en vez de
        /// dejar todos los muros como piezas sueltas indistinguibles. El espesor viaja en el
        /// juego de propiedades.
        /// </remarks>
        public static Seccion ObtenerPano(
            EscritorPaso p, PanoIfc pano,
            Dictionary<string, Seccion> cache,
            Dictionary<string, Ref> materiales,
            Ref dueno)
        {
            var nombre = string.IsNullOrWhiteSpace(pano.Seccion) ? null : pano.Seccion.Trim();
            var clave = "pano|" + pano.Clase + "|" + (nombre ?? string.Empty);

            if (cache.TryGetValue(clave, out var ya))
            {
                return ya;
            }

            var esMuro = pano.Clase == ClaseIfc.Muro;

            var props = new List<object?>
            {
                Texto(p, "Seccion", nombre),
                Texto(p, "Forma", esMuro ? "Muro" : "Losa"),
                Largo(p, "Espesor", pano.EspesorM),
                Texto(p, "Material", pano.Material)
            };

            var juego = p.Ent("IFCPROPERTYSET",
                IfcGuid.Estable("props|" + clave), dueno, JuegoDePropiedades, null,
                EscritorPaso.L(props.ToArray()));

            // IfcWallType / IfcSlabType [10]: GlobalId, OwnerHistory, Name, Description,
            //   ApplicableOccurrence, HasPropertySets, RepresentationMaps, Tag, ElementType,
            //   PredefinedType
            var tipo = p.Ent(esMuro ? "IFCWALLTYPE" : "IFCSLABTYPE",
                IfcGuid.Estable("tipo|" + clave), dueno, nombre, null, null,
                EscritorPaso.L(juego), null, nombre, null,
                new Enumeracion(esMuro ? "SHEAR" : "FLOOR"));

            var material = Material(p, pano.Material, materiales);

            p.Ent("IFCRELASSOCIATESMATERIAL",
                IfcGuid.Estable("material|" + clave), dueno, null, null,
                EscritorPaso.L(tipo), material);

            var hecha = new Seccion
            {
                Clave = clave,
                Nombre = nombre,
                Perfil = null,
                Tipo = tipo
            };

            cache[clave] = hecha;

            return hecha;
        }

        private static Ref Material(
            EscritorPaso p, string nombre, Dictionary<string, Ref> cache)
        {
            var limpio = string.IsNullOrWhiteSpace(nombre) ? "Sin material" : nombre.Trim();

            if (cache.TryGetValue(limpio, out var ya))
            {
                return ya;
            }

            // IfcMaterial [3]: Name, Description, Category
            var hecho = p.Ent("IFCMATERIAL", limpio, null, null);
            cache[limpio] = hecho;

            return hecho;
        }

        private static Ref Propiedades(EscritorPaso p, SeccionIfc s, Ref dueno)
        {
            var props = new List<object?>
            {
                Texto(p, "Seccion", s.Nombre),
                Texto(p, "Forma", s.Forma.ToString()),
                Largo(p, "Ancho", s.AnchoM),
                Largo(p, "Peralte", s.PeralteM),
                Texto(p, "Material", s.Material)
            };

            if (s.PatinM > 1e-6)
            {
                props.Add(Largo(p, "Patin", s.PatinM));
            }

            if (s.AlmaM > 1e-6)
            {
                props.Add(Largo(p, "Alma", s.AlmaM));
            }

            if (s.ParedM > 1e-6)
            {
                props.Add(Largo(p, "Pared", s.ParedM));
            }

            // IfcPropertySet [5]: GlobalId, OwnerHistory, Name, Description, HasProperties
            return p.Ent("IFCPROPERTYSET",
                IfcGuid.Estable("props|" + s.Clave), dueno, JuegoDePropiedades, null,
                EscritorPaso.L(props.ToArray()));
        }

        // IfcPropertySingleValue [4]: Name, Description, NominalValue, Unit
        private static Ref Texto(EscritorPaso p, string nombre, string? valor) =>
            p.Ent("IFCPROPERTYSINGLEVALUE", nombre, null,
                new Crudo("IFCLABEL(" + EscritorPaso.Cadena(valor ?? string.Empty) + ")"), null);

        private static Ref Largo(EscritorPaso p, string nombre, double metros) =>
            p.Ent("IFCPROPERTYSINGLEVALUE", nombre, null,
                new Crudo("IFCLENGTHMEASURE(" + EscritorPaso.Real(metros) + ")"), null);
    }
}
