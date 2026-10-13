# Hidden Magic renderer benchmark

Pass an absolute MagicStatusbarAdapter.dll path and a label. The form remains hidden; its layered surface stays outside all monitors. No input is injected and no live toolbar is attached. Measures repeated visual comparisons and layered transition submissions. Optionally pass a frame output directory or `--repeat` for five iterations in one process. The current module also checks hidden buffer release and subsequent reconstruction.

Use the same Release build, DPI fixture and workload for baseline and new modules. Interpret private/working-set values as benchmark-process values, managed allocated bytes as cumulative allocations, and CPU time within platform timing precision. This is not a whole-application benchmark or a display FPS measurement.
