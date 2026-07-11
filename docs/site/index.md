---
layout: home

hero:
  name: "Conveyor.Batch"
  text: "Reliable batch processing for .NET 8+"
  tagline: Chunk-oriented processing, restartability, partitioning, and dead-lettering as first-class citizens — the Spring Batch equivalent for the .NET ecosystem.
  image:
    src: /icon.svg
    alt: Conveyor.Batch
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/Conveyor-Batch/Conveyor.Batch

features:
  - title: Chunk-Oriented Processing
    details: Read → process → write in configurable commit intervals, with bounded memory usage regardless of input size.
  - title: Restartability
    details: Jobs resume from the last committed checkpoint after a failure, with no duplicate processing.
  - title: Partitioning
    details: Split large datasets and process partitions in parallel with RangePartitioner and LocalPartitionHandler.
  - title: Skip & Retry Policies
    details: Handle bad records and transient failures without aborting the whole job.
  - title: Dead-Lettering
    details: Poison items are routed to an inspectable dead-letter sink, not silently dropped.
  - title: Observable
    details: OpenTelemetry-native via ActivitySource and Metrics — no extra packages required.
---

## Install

```bash
dotnet add package Conveyor.Batch
dotnet add package Conveyor.Batch.Hosting   # optional: Worker Service integration
dotnet add package Conveyor.Batch.IO        # optional: flat-file / JSON / XML IO
```

Continue with the [Getting Started guide](/guide/getting-started) to build your first pipeline.
