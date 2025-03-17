# Integrating OpenTelemetry & Security in eShop

This project aimed to integrate Open Telemetry tracing and security measures in an already developed solution, in this case, in the eShop microservices system. This file details the instructions to setup and run the project.

### Prerequisites

- .NET 9 SDK
- Docker Desktop
- Visual Studio 2022 version 17.10 or newer

In the [original repo](https://github.com/dotnet/eShop) there is a tutorial to make sure the prerequisites are correctly installed and started.

### Running the solution

1. **Clone the repository:**
   ```sh
   git clone https://github.com/SardinhaAlmeida/eShop_AS_1.git
   cd eShop_AS_1
   ```

2. **Open `eShop.sln` project with VisualStudio:**
  Click on Start Project to run it

3. **Start OpenTelemetry Collector, Prometheus, Grafana and Jaeger Services that were configured:**
  Make sure you are on the project root folder `/eShop_AS_1`
  ```sh
  docker-compose up -d
  ```

4. **Verify if services are running correctly:**

- Aspire Dashboard: https://localhost:19888
- WebApp: https://localhost:7928
- Prometheus: http://localhost:9090
- Jaeger UI: http://localhost:16686
- Grafana UI: http://localhost:3000

5. **Viewing the Grafana Dashboard:**

Grafana is already configured with three dashboards to display OpenTelemetry metrics and traces. It is only necessary to:

1. Open http://localhost:3000 in your browser.  
2. Login using default credentials: (admin, admin).
3. Go to **Dashboard** and select the dashbard wanted:
    - *Dashboard*: Shows metrics
    - *Traces Basket.API* or *Traces Ordering.API*: Shows traces


# 

The report can be found in the root of the project with the name **AS_Assig1_Report_108796.pdf**.