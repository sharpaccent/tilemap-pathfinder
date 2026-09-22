# Tilemap Pathfinder

A lightweight pathfinding solution built around Unity's Tilemap system.

## Features

* Works with any Unity Tilemap layout, including:

  * Square grids
  * Isometric grids
  * Hexagonal grids
* Grid type agnostic. The pathfinder relies on Unity's Tilemap API for coordinate conversion, so the same calls work across different tile layouts.
* No practical grid-size limitation beyond `int.MaxValue`.
* Simple API and minimal setup.

## How to Use

Take a look at `PathfinderDebug.cs` for a basic implementation example.

To calculate a path, simply call `FindPath` with:

* A start position
* A destination position
* The `Tilemap` used as your obstacle layer

The pathfinder handles the required coordinate conversion internally through Unity's Tilemap API.

## ToDo

* ~~Scheduler~~
* ~~Path Smoothing~~


## Video Explanation

Part 1
https://youtu.be/41h4SIuH8qc

Part 2
https://youtu.be/a9MIkZAIMiw

Part 3
https://youtu.be/KrKVhV96OC0

Part 4
https://youtu.be/C1zyyofgONs

Part 5
https://youtu.be/FY85eA3pYl0


## Support

If you find this project useful and want to support more projects like this:

Patreon
https://www.patreon.com/csharpaccent

Sharp Accent
https://sharpaccent.com/
