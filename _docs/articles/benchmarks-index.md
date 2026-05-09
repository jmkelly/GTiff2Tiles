# GTiff2Tiles.Benchmarks

**GTiff2Tiles.Benchmarks** contains local performance benchmarks for **GTiff2Tiles.Core**.

## Requirements

- .NET SDK
- Linux x64 / Windows 10+ x64
- An EPSG:4326 GeoTIFF input file

## Options

| Short | Long | Description | Required? |
| :---: | :--- | :---------- | :-------: |
| `-i` | `--input` | Path to input GeoTIFF file | No |
|  | `--single-tile` | Run the short single-tile write benchmark | No |
|  | `--help` | Show command line help | No |
|  | `--version` | Show version information | No |

`-i/--input` is optional. If omitted, the benchmark app uses `Examples/Input/Benchmark.tif`.

## Running benchmarks

Build and run the benchmarks in `Release`:

```bash
dotnet run -c Release --project GTiff2Tiles.Benchmarks
```

That default path keeps the existing full tile pyramid benchmark.

### Run the short single-tile benchmark

Use `--single-tile` to opt in to the faster benchmark intended for regular local iteration:

```bash
dotnet run -c Release --project GTiff2Tiles.Benchmarks -- --single-tile
```

To use a different GeoTIFF:

```bash
dotnet run -c Release --project GTiff2Tiles.Benchmarks -- --single-tile --input "/path/to/input.tif"
```

The single-tile benchmark measures writing one PNG tile from the source GeoTIFF and is intentionally short-running so it can be used during performance tuning.
