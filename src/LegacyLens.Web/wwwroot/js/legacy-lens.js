// Puente mínimo con Mermaid. Se renderiza a demanda desde Blazor en lugar de
// dejar que Mermaid recorra el DOM al cargar: los grafos aparecen cuando el
// componente ya ha pintado su contenedor.
window.legacyLens = {
    renderMermaid: async function (elementId, definition) {
        const host = document.getElementById(elementId);
        if (!host || typeof mermaid === 'undefined') {
            return;
        }

        try {
            const { svg } = await mermaid.render(elementId + '-svg', definition);
            host.innerHTML = svg;
        } catch (error) {
            console.error('Mermaid no pudo dibujar el grafo', error);
            host.innerHTML =
                '<p class="text-danger small">No se pudo dibujar el grafo.</p>';
        }
    }
};
