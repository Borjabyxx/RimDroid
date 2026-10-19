# Uso del kit auditado

El encargo original se conserva en `CODEX_TAREA.md` como referencia histórica. La versión actual es 0.3 experimental y su compilación real está comprobada; todavía no se ha ejecutado en el juego ni Android.

- Instalación del ZIP ya generado: `INSTALAR.md`.
- Build local y configuración de Actions: `README.md`.
- Auditoría y pruebas realizadas: `AUDITORIA.md`.
- Pruebas dentro del juego pendientes: `PRUEBAS.md`.

El mod no requiere cambios del APK. Mantén `mods.zip` y sus tres DLL fuera del repositorio y de cualquier artefacto publicado. El script `tools/build_from_zip.py` compila con SDK .NET 10 y crea una distribución que solo incluye la DLL original del mod, About, instrucciones y licencia.
