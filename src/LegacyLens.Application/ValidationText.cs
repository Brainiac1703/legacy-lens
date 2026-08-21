namespace LegacyLens.Application;

/// <summary>
/// Clase marcadora de los mensajes de validación.
///
/// Los mensajes de validación los ve el usuario, así que van en recursos igual
/// que el resto de la interfaz. Pero viven **aquí** y no en el proyecto web
/// porque nacen aquí: es el validador el que decide qué está mal, y si mañana
/// hubiera una API además de la web, ambos darían el mismo mensaje.
///
/// Está en la raíz del proyecto por el mismo motivo que UiText en la web: una
/// clase dentro del espacio de nombres Resources haría que el localizador
/// buscara Resources/Resources.
///
/// Los mensajes propios de FluentValidation —los de NotEmpty, MaximumLength y
/// compañía— no hacen falta aquí: la librería trae sus propias traducciones y las
/// elige por la cultura de la interfaz.
/// </summary>
public sealed class ValidationText
{
    private ValidationText() { }
}
