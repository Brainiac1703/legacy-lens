namespace LegacyLens.Web;

/// <summary>
/// Clase marcadora de los textos de la interfaz.
///
/// No tiene miembros y no se instancia: solo sirve para que
/// <c>IStringLocalizer&lt;UiText&gt;</c> sepa qué juego de ficheros .resx buscar.
///
/// Está en la raíz del proyecto y no dentro de Resources a propósito. El
/// localizador compone el nombre del recurso como
/// «espacioDeNombresRaíz + ResourcesPath + tipo sin el espacio raíz», así que una
/// clase en LegacyLens.Web.Resources acabaría buscando Resources/Resources/UiText.
/// Es un fallo silencioso —devuelve la clave en lugar del texto— y cuesta ver.
///
/// Se eligió un recurso compartido en lugar de uno por componente porque muchos
/// textos (niveles de riesgo, tipos de objeto, avisos) aparecen en varias páginas
/// y habría que duplicarlos y mantenerlos sincronizados.
///
/// El español vive en UiText.resx, que es el recurso neutro: si algún día se
/// añade una cultura sin traducir, el usuario verá español y no la clave.
/// </summary>
public sealed class UiText
{
    private UiText() { }
}
