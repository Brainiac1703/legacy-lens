---
name: add-test
description: Añadir un test automático a Legacy Lens, con el nombre y la ubicación que este proyecto usa. Úsala cuando haya que cubrir lógica determinista nueva.
---

# Añadir un test

## Dónde

Hay **un solo proyecto de pruebas**: `tests/LegacyLens.Analysis.Tests`. Referencia
`LegacyLens.Analysis` y `LegacyLens.Domain`, y nada más.

| Fichero | Qué cubre |
| --- | --- |
| `TSqlAnalyzerTests.cs` | el analizador sobre `samples/legacy-erp.sql` |
| `WarehouseSampleTests.cs` | el segundo ejemplo y su perfil de riesgo |
| `DependencyGraphTests.cs` | los recorridos del grafo, con aristas escritas a mano |

Si lo que pruebas es un recorrido o un cálculo, va en `DependencyGraphTests`. Si es cómo se
interpreta un script, en el fichero del ejemplo correspondiente.

## El nombre

**En inglés, y es la regla que más se escurre**, porque un nombre de test se lee como una
frase y apetece escribirla en el idioma en que se piensa. Los 33 que hay estuvieron en
castellano hasta que alguien lo señaló.

```
The_nightly_process_is_the_riskiest_object      sí
El_proceso_nocturno_es_el_objeto_de_mayor_riesgo no
```

Una frase que afirma qué debe pasar, con guiones bajos. Las variables locales del test
también en inglés.

## El patrón

```csharp
[Fact]
public void Upstream_transitive_closure_is_the_impact_radius()
{
    var affected = DependencyGraph.TransitiveClosure(
        Graph, "dbo.CalcularIva", DependencyGraph.Direction.Upstream);

    Assert.Contains("dbo.Facturar", affected);
}
```

## Qué se prueba y qué no

- **Solo lo determinista.** El analizador, la puntuación de riesgo y los recorridos del grafo
  se prueban con asserts porque dos ejecuciones dan lo mismo. Lo que produce el modelo de
  lenguaje **no** se prueba así: se mide con el arnés de `tools/LegacyLens.Evals`. Un test que
  afirme algo sobre el texto que genera el modelo va a fallar de forma intermitente y hará que
  se ignore la suite entera.
- **Estado real de la cobertura, para que nadie se lleve una sorpresa:** los 51 tests cubren
  `Domain` y `Analysis`. `Application`, `Persistence.EF`, `Ai`, `Mcp` y `Web` no tienen
  ninguno. Está reconocido en `docs/trazabilidad-temario.md` con un ◐, no escondido. Si añades
  lógica a `Application`, hará falta un proyecto de pruebas nuevo.
- **Nada de base de datos ni de red en un test.** El grafo se prueba con aristas escritas a
  mano justo por eso: no hace falta ni SQL Server ni credenciales.

## El volcado de diagnóstico

Cada fichero de ejemplo tiene un `Diagnostic_dump` que imprime el análisis completo: objetos,
métricas, riesgo y factores. **Antes de escribir un assert sobre una cifra, ejecútalo y mira
el número real.** El segundo ejemplo se diseñó a ojo la primera vez y se quedó en riesgo 60
cuando la intención era llegar a crítico; el volcado lo dejó claro en un minuto.

```bash
dotnet test LegacyLens.slnx --filter "FullyQualifiedName~Diagnostic_dump" --logger "console;verbosity=detailed"
```

## Comprobar

```bash
dotnet test LegacyLens.slnx
```

Es lo mismo que ejecuta el CI. Si pasa en local, pasa allí.
