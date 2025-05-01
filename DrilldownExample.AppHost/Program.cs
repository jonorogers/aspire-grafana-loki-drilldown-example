using DrilldownExample.AppHost.OpenTelemetryCollector;

var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.DrilldownExample_ApiService>("apiservice");

builder.AddProject<Projects.DrilldownExample_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);


var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.2.1")
           .WithBindMount("../prometheus", "/etc/prometheus", isReadOnly: true)
           .WithArgs("--web.enable-otlp-receiver", "--config.file=/etc/prometheus/prometheus.yml")
           .WithHttpEndpoint(targetPort: 9090, name: "http");

var loki = builder.AddContainer("loki", "grafana/loki:latest")
                  .WithBindMount("../loki/config.yaml", "/etc/loki/config.yaml", isReadOnly: true)
                  .WithHttpEndpoint(targetPort: 3100, name: "http")
                  .WithArgs("-config.file=/etc/loki/config.yaml");


var jaeger = builder.AddContainer("jaeger", "jaegertracing/jaeger:latest")
                    .WithBindMount("../jaeger/config.yaml", "/jaeger/config.yaml", isReadOnly: true)
                    .WithBindMount("../jaeger/config-ui.json", "/cmd/jaeger/config-ui.json", isReadOnly: true)
                    .WithHttpEndpoint(targetPort: 16686, name: "http")
                    // Use a different port to avoid conflict with the otel collector
                    .WithEndpoint(port: 5317, targetPort: 4317, name: "grpc-collector")
                    .WithHttpEndpoint(targetPort: 5778, name: "http-sampling")
                    .WithHttpEndpoint(targetPort: 9411, name: "http-api")
                    .WithArgs("--config=/jaeger/config.yaml");


var grafana = builder.AddContainer("grafana", "grafana/grafana")
                     .WithBindMount("../grafana/config", "/etc/grafana", isReadOnly: true)
                     .WithBindMount("../grafana/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
                     .WithEnvironment("PROMETHEUS_ENDPOINT", prometheus.GetEndpoint("http"))
                     .WithEnvironment("LOKI_ENDPOINT", loki.GetEndpoint("http"))
                     .WithEnvironment("JAEGER_ENDPOINT", jaeger.GetEndpoint("http"))
                     .WithEnvironment("GF_FEATURE_TOGGLES_ENABLE", "accessControlOnCall")
                     .WithEnvironment("GF_PLUGINS_PREINSTALL_DISABLED", "true")
                     .WithEnvironment("GF_INSTALL_PLUGINS", "https://storage.googleapis.com/integration-artifacts/grafana-lokiexplore-app/grafana-lokiexplore-app-latest.zip;grafana-lokiexplore-app")
                     .WithHttpEndpoint(targetPort: 3000, name: "http");


builder.AddOpenTelemetryCollector("otelcollector", "../otelcollector/config.yaml")
       .WithEnvironment("PROMETHEUS_ENDPOINT", $"{prometheus.GetEndpoint("http")}/api/v1/otlp")
       .WithEnvironment("JAEGER_ENDPOINT", $"{jaeger.GetEndpoint("grpc-collector").Property(EndpointProperty.HostAndPort)}")
       .WithEnvironment("LOKI_ENDPOINT", $"{loki.GetEndpoint("http")}/otlp");

builder.Build().Run();
