# Directrices de este proyecto (para gemini y agentes)

## Metodología para Modificaciones Extensas o de Gran Tamaño

Para cualquier cambio, refactorización o lote de mejoras de tamaño mediano o grande en el código (especialmente en www/index.html):

1. **Descomposición en bloques pequeños y modulares:**
   - Dividir la tarea global en fases o bloques lógicos y acotados (Bloque 1, Bloque 2, Bloque 3...).

2. **Ejecución continua y encadenada automática:**
   - Implementar el bloque actual.
   - Validar la sintaxis y coherencia inmediatamente (mediante Node.js u otras herramientas pertinentes).
   - Iniciar inmediatamente el siguiente bloque de manera autónoma, sin detenerse a pedir confirmación al usuario para cada paso a menos que surja una duda o bloqueo crítico.

3. **Trazabilidad clara:**
   - Notificar brevemente el progreso de cada hito completado conforme se avanza.

4. **Verificación final antes de compilar:**
   - Asegurar que todo el bloque de cambios quede probado y sintácticamente validado antes de indicar al usuario que ejecute el workflow de GitHub Actions (`runflow`).

## Reglas Obligatorias de Publicación y Binarios Locales (`publish/`)

Siempre que se actualice o genere el ejecutable local en la carpeta `publish/` para que el usuario pruebe la aplicación:

1. **Despliegue Estrictamente Autónomo (Self-Contained):**
   - La publicación debe ejecutarse obligatoriamente con el flag `--self-contained true` para la arquitectura `win-x64`:
     ```powershell
     dotnet publish TarkovHelper\TarkovHelper.csproj -c Release -r win-x64 --self-contained true -o publish
     ```
   - **NUNCA** copiar binarios dependientes de framework (`frameworks`) a `publish/`.

2. **Verificación Obligatoria de `TarkovHelper.runtimeconfig.json`:**
   - Todo agente debe verificar que `publish\TarkovHelper.runtimeconfig.json` contenga la propiedad `"includedFrameworks"`.
   - Si contiene `"frameworks"`, el host de Windows entrará en conflicto con las DLLs locales y mostrará la ventana de error *"You must install or update .NET to run this application"*, incluso aunque el usuario tenga .NET instalado en el sistema.

3. **Protección de la Base de Datos Local:**
   - Asegurarse de que `publish\tarkov_data.db` no sea sobreescrita ni eliminada accidentalmente al publicar, preservando las notas, progreso y configuraciones del usuario.

## Regla Estricta de Integridad de Datos (Prohibición de Datos Históricos)

1. **Prohibido el uso o restauración de datos desde git history / copias históricas:**
   - Queda estrictamente prohibido extraer, comparar o sobreescribir datos usando commits antiguos (`git show origin/main:...`, `baseline.db` o volcados históricos).
   - El juego evoluciona constantemente (nombres de misiones, cambios de objetivos, nuevos requisitos y mapas). La única fuente válida de actualización y verdad son las **APIs vivas actuales** (TarkovTracker / Tarkov.dev) y la base de datos viva actual con las aportaciones del usuario.
   - Cualquier intento de "restaurar" datos del pasado queda terminantemente vetado para evitar reintroducir misiones obsoletas o borrar misiones nuevas.
