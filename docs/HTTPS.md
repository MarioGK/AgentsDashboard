# HTTPS & Certificates

- AgentsDashboard and TerraScale share the same local mkcert certificate at `~/.local/share/mkcert/terrascale-dev.pem` (SANs: localhost,127.0.0.1,::1,192.168.10.101,terrascale-dev).
- `dotnet dev-certs` cannot import this certificate (missing ASP.NET OID), so AgentsDashboard uses `ASPNETCORE_Kestrel__Certificates__Default__Path` and `ASPNETCORE_Kestrel__Certificates__Default__KeyPath`.
- CA is system-trusted with `mkcert -install`; clients trust `~/.local/share/mkcert/rootCA.pem`.
- AgentsDashboard ControlPlane launch profile sets `DOTNET_GENERATE_ASPNET_CERTIFICATE=false`.
