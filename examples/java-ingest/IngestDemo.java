import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.time.Instant;
import java.util.UUID;

/**
 * LogPilot HTTP JSON 批量接入示例（JDK 11+，无第三方依赖）。
 *
 * <p>用法：
 * <pre>
 *   set LOGPILOT_URL=http://127.0.0.1:5080
 *   set TOKEN=lp_你的接入密钥
 *   java IngestDemo.java
 * </pre>
 */
public final class IngestDemo {

    public static void main(String[] args) throws Exception {
        String baseUrl = env("LOGPILOT_URL", "http://127.0.0.1:5080");
        String token = env("TOKEN", "");
        String serviceName = env("SERVICE_NAME", "java-demo");
        int count = Integer.parseInt(env("COUNT", "5"));

        if (token.isBlank()) {
            System.err.println("缺少 TOKEN，请先设置环境变量 TOKEN=lp_xxx");
            System.exit(1);
        }

        String body = buildBody(serviceName, count);

        HttpClient client = HttpClient.newHttpClient();
        HttpRequest request = HttpRequest.newBuilder()
                .uri(URI.create(baseUrl.replaceAll("/+$", "") + "/api/v1/ingest/logs"))
                .header("Authorization", "Bearer " + token)
                .header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofString(body, StandardCharsets.UTF_8))
                .build();

        HttpResponse<String> response = client.send(request, HttpResponse.BodyHandlers.ofString());
        System.out.println("HTTP " + response.statusCode());
        System.out.println(response.body());
        if (response.statusCode() >= 300) {
            System.exit(1);
        }
    }

    private static String buildBody(String serviceName, int count) {
        String traceId = UUID.randomUUID().toString().replace("-", "");
        StringBuilder logs = new StringBuilder();
        for (int i = 1; i <= count; i++) {
            if (i > 1) {
                logs.append(',');
            }
            String level = (i % 5 == 0) ? "Error" : "Info";
            logs.append('{')
                    .append("\"timestamp\":\"").append(Instant.now().toString()).append("\",")
                    .append("\"serviceName\":\"").append(escape(serviceName)).append("\",")
                    .append("\"environment\":\"dev\",")
                    .append("\"level\":\"").append(level).append("\",")
                    .append("\"message\":\"").append(escape("[demo] (" + i + "/" + count + ") hello LogPilot from java-ingest")).append("\",")
                    .append("\"traceId\":\"").append(traceId).append("\",")
                    .append("\"attributes\":{\"module\":\"java-ingest\",\"seq\":").append(i).append('}')
                    .append('}');
        }
        return "{\"logs\":[" + logs + "]}";
    }

    private static String env(String name, String fallback) {
        String value = System.getenv(name);
        return (value == null || value.isBlank()) ? fallback : value.trim();
    }

    private static String escape(String value) {
        return value.replace("\\", "\\\\").replace("\"", "\\\"");
    }

    private IngestDemo() {
    }
}
