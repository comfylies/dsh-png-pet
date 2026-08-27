"""Batch-remove image backgrounds locally with rembg without overwriting inputs."""

from __future__ import annotations

import argparse
from pathlib import Path

from rembg import new_session, remove


SUPPORTED_EXTENSIONS = {".avif", ".bmp", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp"}


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input_directory", type=Path)
    parser.add_argument("output_directory", type=Path)
    parser.add_argument("--model", default="u2net")
    return parser.parse_args()


def main() -> None:
    arguments = parse_arguments()
    input_directory = arguments.input_directory.resolve()
    output_directory = arguments.output_directory.resolve()

    if not input_directory.is_dir():
        raise ValueError("input_directory must exist and be a directory")
    if input_directory == output_directory:
        raise ValueError("output_directory must be different from input_directory")

    images = sorted(
        path for path in input_directory.rglob("*")
        if path.is_file() and path.suffix.lower() in SUPPORTED_EXTENSIONS
    )
    if not images:
        raise ValueError("input_directory contains no supported image files")

    session = new_session(arguments.model)
    for source in images:
        relative_path = source.relative_to(input_directory)
        destination = output_directory / relative_path.with_suffix(".png")
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(remove(source.read_bytes(), session=session))


if __name__ == "__main__":
    main()
