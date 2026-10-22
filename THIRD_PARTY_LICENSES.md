# Dependências e licenças

| Componente | Uso | Licença |
|---|---|---|
| .NET 8 (runtime e WPF) | Embutido no executável | MIT |
| FFmpeg 7.1.1 "essentials" (build de gyan.dev / GyanD/codexffmpeg) | Captura e codificação; embutido compactado no `.exe` e baixado pelo workflow | GPL v3 (inclui libx264, GPL) — https://ffmpeg.org/legal.html |
| NAudio 2.2.1 | Captura WASAPI (microfone e som do computador) e reamostragem | MIT |
| System.Security.Cryptography.ProtectedData 8.0.0 | Proteção da chave do Drive (DPAPI) | MIT |
| xunit, Microsoft.NET.Test.Sdk | Apenas testes (não vão no executável) | Apache-2.0 / MIT |

Como o executável distribui um FFmpeg em build GPL, o conjunto é distribuído sob GPL-3.0-or-later. O código-fonte do FFmpeg está em https://ffmpeg.org e o do build usado em https://github.com/GyanD/codexffmpeg.
