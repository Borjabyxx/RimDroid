# Instalación de Autonomous Colonists 0.3 experimental

Este ZIP es un mod independiente para RimWorld 1.7 (.NET 10), no un APK.
La compilación se ha comprobado contra las bibliotecas privadas facilitadas por el usuario.
La ejecución real en RimWorld y Android todavía está pendiente.

1. Extrae el ZIP. Copia `AutonomousColonists/` a la carpeta **Mods del juego** que usa tu instalación de RimDroid, junto a los demás mods. No lo copies a la carpeta de APK ni a `Data/Core`.
2. Comprueba `Mods/AutonomousColonists/About/About.xml` y `Mods/AutonomousColonists/1.7/Assemblies/AutonomousColonists.dll`. Evita un nivel duplicado `AutonomousColonists/AutonomousColonists`.
3. Activa **Autonomous Colonists (Experimental)** en la lista de mods y reinicia el juego. Usa una partida de prueba o una copia de tu guardado.
4. En **Trabajo**, activa las prioridades numéricas. En **Opciones → Mods → Autonomous Colonists**, activa la automatización (apagada por defecto en cada partida) y pulsa **Recalcular ahora**.
5. El mod solo gestiona trabajos ya habilitados (prioridades 1–4). Los valores 0 y las incapacidades se conservan. Las ediciones manuales que difieren del último valor del mod quedan bloqueadas; se observan cada 250 ticks y siempre antes del recálculo.
6. Puedes excluir colonos, ajustar frecuencia/estabilidad y la cobertura deseada por trabajo. Las exclusiones siguen al colono entre mapas. La cobertura es un objetivo del jugador; el mod no mide existencias ni crea encargos de producción.
7. Con Work Manager (`LordKuper.WorkManager`) activo, este gestor se suspende. Desactiva otros gestores de prioridades si quieres probarlo de forma aislada.
8. Antes de desinstalar: mantén activadas las prioridades numéricas, desactiva la automatización, deja transcurrir 250 ticks si no has abierto los ajustes y guarda una copia nueva. Solo se restauran celdas aún controladas; los cambios manuales se conservan. Después retira el mod. Si los números están apagados, la restauración espera a que los reactives: el juego oculta entonces los valores individuales.

Los trabajos forzados, necesidades, horarios, dieta, ropa, medicina y `selfTend` siguen bajo control del juego/jugador. Una edición al mismo valor que la última asignación, o varios cambios que vuelven a ese valor entre observaciones, no pueden detectarse sin interceptar la interfaz; excluye ese colono si necesitas proteger todas sus decisiones. No se usa Harmony.

Si hay un error, la automatización se pausa e intenta restaurar sus celdas. Desactiva y vuelve a activar para reintentar, tras revisar el registro. No se cambian partidas fuera del juego ni se exige modificar RimDroid.
