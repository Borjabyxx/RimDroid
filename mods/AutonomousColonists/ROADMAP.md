# Próximas fases

v0.5 integra acciones nativas de comida, camas, medicina, ropa, rescate y seguridad local; consultar AUTONOMIA_0.5.md para su alcance y validación pendiente. La lista inferior conserva también investigaciones anteriores; no implica que defensa armada o equipamiento militar estén implementados.

1. Validar carga, opciones, órdenes forzadas, guardar/cargar, migración y restauración en RimWorld 1.7 real, en PC y Android por separado.
2. Medir el adaptador completo con 1/5/20/50 colonos en el dispositivo Android; el benchmark actual solo mide la lógica pura en Linux x64.
3. Mantener pruebas públicas en Actions; un runner privado queda aplazado hasta que se necesite automatizar compilaciones con referencias legales privadas.
4. Afinar cobertura a partir de demanda real de encargos, estaciones y reservas, con cachés e intervalos medidos. Hoy la demanda es configurable por el jugador.
5. Explorar interfaz de bloqueos por celda e integración voluntaria con otros gestores; no interceptar la UI sin justificarlo.
6. v0.4 observa alimentación/descanso y señales de seguridad y genera propuestas. Implementar disponibilidad real y ejecución reversible solo en una fase posterior con pruebas específicas. Medicina y ropa siguen pendientes.

Invariantes: no publicar archivos del juego; no habilitar prioridades cero; preservar cambios manuales observables; restaurar solo celdas aún controladas; no crear/sustituir trabajos forzados ni alterar RimDroid; no atribuir al Android mediciones hechas en otro sistema.
