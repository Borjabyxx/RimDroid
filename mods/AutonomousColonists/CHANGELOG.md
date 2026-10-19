# Cambios

## 0.4.0-experimental — 19/10/2026

- AutonomousDecisionCoordinator puro y AutonomousCoordinatorComponent por partida; se conserva intacto el adaptador histórico v0.2.
- Director voluntario y observación por defecto; Equilibrado predeterminado, con perfiles Conservador y Avanzado.
- Observación real de comida, descanso y contexto de salud/trabajo; SafetyAutonomy representa peligro, incendio, refugio propuesto, permisos defensivos y fin de emergencia.
- Candidatos tipados, orden determinista, arbitraje único por actividad, histéresis, estabilidad e incumbencia ante alternativas marginales.
- Ningún ejecutor nuevo de trabajos/necesidades/seguridad; no hay opción que habilite acciones inseguras. Health/Apparel reservados para fases posteriores.
- Puerta común de permisos del gestor v0.3: observación, errores del director, órdenes forzadas y reclutamiento impiden escrituras y aplazan restauraciones conservando su historial.
- Persistencia mínima por ID/mapa; cachés invalidadas por traslados, exclusiones, salidas y cambios de perfil. Diagnósticos de sesión limitados.
- Interfaz desplazable con botones de 40 px, perfiles, intervalo, estado, propuestas, motivo, tiempos/memoria y exclusiones compartidas.
- 40 pruebas C# originales intactas, 50 adicionales, 10 Python y escenarios aleatorios; benchmarks 5/20/50. Nuevo workflow público separado; workflows existentes intactos.
- Build con referencias privadas reales .NET 10, cero advertencias. Ensayo Scribe escribe XML pero lectura bloqueada por dependencias originales ausentes; juego y Android siguen pendientes.

## 0.3.0-experimental — 19/10/2026

- Compilación real contra RimWorld 1.7/.NET 10 con advertencias tratadas como errores.
- Activación voluntaria por partida; solo gestiona trabajos previamente habilitados.
- Asignación pura y determinista: habilidades/pasiones, capacidad y carga de roles, cobertura configurable, suplentes e histéresis.
- Las prioridades manuales cuentan para la cobertura; los bloqueos sobreviven a exclusiones, pausas y traslados. Observa también celdas no gestionadas.
- Registro único por partida, referencias persistentes a colonos y compatibilidad de lectura del antiguo componente v0.2.
- Observación cada 250 ticks; recálculo por intervalo o cambios relevantes. Pausa por estrés e histéresis de recuperación.
- Pausa cuando el juego oculta las prioridades numéricas; conserva la información para restaurar después.
- Eliminadas las escrituras médicas irreversibles de AutoSelfTend. Necesidades, órdenes forzadas, horarios y políticas del juego conservan su autoridad.
- Interfaz desplazable ajustada a su altura real, exclusiones, cobertura, liberación voluntaria de bloqueos y métricas.
- Pruebas automatizadas, benchmark de 1/5/20/50 colonos, auditoría PE estática y ZIP con licencia/instrucciones.
- Workflow separado para pruebas públicas y distribución con referencias privadas en runner dedicado. Sin cambios de APK ni workflows previos.
- Diagnóstico opcional de Scribe: XML escrito, lectura bloqueada por Steamworks/Brutal.Core.Logging ausentes; no se presenta como prueba superada.
- Primera ejecución pública de Actions superada; build remoto privado y ejecución real en juego/Android pendientes.

## 0.2.0-source — kit de entrada

Prototipo fuente: reparto horario, exclusiones y bloqueos por mapa, configuración y autocuidado médico. No acompañaba una DLL confirmada ni resultados de juego.
