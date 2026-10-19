# Encargo para Codex — Autonomous Colonists (RimWorld 1.7 / Android)

**Modelo recomendado:** Codex con razonamiento **alto** para esta primera auditoría: APIs nuevas de Waylith, cambios en .NET 10, serialización de partidas y regresiones de comportamiento; en ajustes posteriores menores, razonamiento medio para ahorrar créditos.

Trabaja sobre el repositorio `https://github.com/Borjabyxx/RimDroid`, rama base `fix/isorim-waylith-android` (sin modificar `master`), usando el ZIP adjunto del **código fuente** Autonomous Colonists v0.2 y el ZIP **privado** `mods.zip` facilitado por el usuario. En este ZIP privado están `Assembly-CSharp.dll`, `Waylith.dll` y `UnityStubs.dll` de SU instalación de RimWorld 1.7. No supongas que los archivos adjuntos a ChatGPT estén automáticamente accesibles para Codex: el usuario debe adjuntarlos en su tarea de Codex, o proporcionarlos únicamente en el entorno seguro de compilación.

## Objetivo funcional

El jugador toma decisiones estratégicas (construir, investigar, explorar); los colonos se ocupan de su funcionamiento cotidiano con el menor número de prioridades manuales posible. Mantén a RimWorld como autoridad de tareas urgentes y órdenes forzadas. En esta fase, consolida **gestión automática de trabajos**, no finjas que alimentación, ropa y descansos avanzados ya estén integrados. Las mejoras posteriores deben mantener rendimiento adecuado para Android ARM64.

## Procedimiento exigido

1. Revisa la rama real y comprueba que no estás alterando el APK de RimDroid, su motor Waylith ni workflows existentes.
2. Trabaja en nueva rama de funcionalidad `feat/autonomous-colonists-1.7` basada en `fix/isorim-waylith-android`, o en repositorio independiente para el mod si es lo más limpio; documenta la decisión. Mantén el mod en un directorio autocontenido.
3. Audita TODO el C# v0.2, `About.xml`, `.csproj` y lógica de persistencia. Verifica mediante compilación **real** las firmas de Verse/RimWorld/Waylith que usa, empleando las tres DLL proporcionadas y .NET SDK 10. Corrige incompatibilidades, advertencias importantes y errores de interfaz de usuario/serialización.
4. Nunca añadas DLL propietarias, `mods.zip`, datos de partida, claves, credenciales o archivos del juego a Git, artefactos publicados o APK. Usa referencias efímeras en el entorno de compilación. Revisa `git status`, contenido del commit y ZIP final.
5. Verifica que el gestor: asigna prioridades 1–4 por habilidad/pasión; distribuye especialistas y suplentes; no activa trabajos deshabilitados; recalcula de forma estable y configurable; respeta exclusiones, cambios manuales y órdenes forzadas; guarda/carga estado; revierte solo celdas aún controladas por el mod; detecta conflicto con Work Manager. Evita sobreescrituras con otros gestores.
6. Investiga y corrige posibles defectos de v0.2: cambios de prioridades sin histéresis, especialidades repartidas sin tener en cuenta demanda o capacidad disponible, el conteo de especialistas usando elegibles en lugar de población, `AutoSelfTend` activando cambios persistentes no reversibles, condiciones de estrés/recuperación, valores manuales anulados por refrescos, problemas con colonos incorporados o que dejan el mapa.
7. Asegura complejidad contenida: no escanees toda la colonia en cada tick. Optimiza para 1, 5, 20 y 50 colonos y añade métricas del coste de recalculación, sin inventar resultados. No utilices API de IA, Internet, ni Harmony salvo justificación técnica.
8. Escribe pruebas automatizadas para la lógica pura de asignación; las pruebas que dependan del runtime del juego deben documentarse como manuales. Haz un plan de pruebas en Android: 1.7 inicia, carga mod, ajustes, nueva partida, cargar/guardar, 1/5/20/50 colonos, prioridad editada manualmente, exclusiones, múltiples mapas, emergencias, recuperación de errores. Mantén partidas antiguas intactas.
9. Genera solo `AutonomousColonists/` con `About/About.xml` y `1.7/Assemblies/AutonomousColonists.dll` (y los recursos originales necesarios), **sin** las DLL propietarias de RimWorld. Incluye SHA-256, informes de build/pruebas y resultados exactos. No afirmes "probado en Android" sin una ejecución real en Android.
10. No modifiques `master`, no hagas merge ni despliegue, no ejecutes un workflow para reconstruir el APK sin necesidad; entrega rama/PR con resumen de cambios y pasos de instalación del mod separado.

## Requisitos de diseño

- Defaults conservadores y opción general de desactivar automatización; respetar decisiones del jugador.
- Sistema determinista, predecible, sin cambios de trabajo constantes ni bucles de prioridades; sin interferir con vanilla al fallar.
- Tratamientos, hambre, descanso y selección de ropa: conservar el comportamiento vanilla durante la fase inicial; preparar interfaces extensibles para su integración futura.
- No copiar ni redistribuir código de Work Manager sin comprobar y cumplir su licencia. Implementación independiente inspirada en funcionalidades, no fusión textual.
- Priorizar una compilación verificable y un mod instalable por separado sobre promesas de prestaciones sin pruebas.

## Estado de partida

- Fuente: versión v0.2 experimental, todavía **sin** DLL compilada confirmada.
- Referencias: el usuario ha proporcionado exactamente las 3 DLL solicitadas, pero **no** se ha ejecutado `dotnet build` en el entorno de ChatGPT porque no dispone del SDK .NET.
- RimDroid Android 1.7: el usuario confirma que el juego arranca y funciona en su dispositivo; todavía NO se ha probado nuestro mod.
- Rama de RimDroid localizada: `fix/isorim-waylith-android`, último commit verificado al recibir el enlace `98199cb15f7b7e4cfed246839515ca5980d3a97a`.

## Entrega de Codex

Informe detallando: qué compila, errores resueltos, pruebas realizadas y no realizadas, riesgos, SHA-256 de artefacto, cómo instalar y activar el mod, enlace a PR/commit sin fusionarlo y una lista de siguientes mejoras recomendadas. Si falta alguna otra referencia del runtime, identificarla por nombre exacto y por qué se necesita, sin inventarla.
