# Cambios

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
