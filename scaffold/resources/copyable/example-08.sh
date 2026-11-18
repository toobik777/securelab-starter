docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet run --project src/SecureLab.Api -- --reset-database
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
dotnet run --project src/SecureLab.Api
