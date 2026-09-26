---
tags: [imrdy-expert/build, imrdy-expert/linux]
summary: "build-dev.sh OS-detects and publishes a Linux binary to ~/.local/bin/imrdy — atomic swap via temp-in-same-dir + mv, and a daemon stop sequence that escalates SIGTERM to SIGKILL and then refuses to deploy (exit 1) rather than relaunching over a daemon it could not stop"
last-verified: "2026-09-25"
---

## build-dev.sh Cross-Platform Deploy

[`build-dev.sh`](../../../build-dev.sh) (repo root) detects the host OS via `uname -s` and branches
the whole publish/deploy sequence. A new Linux-side component extends this Linux branch rather than
adding OS detection of its own.

```bash
case "$(uname -s)" in
    Linux*)               PLATFORM=linux ;;
    MINGW*|MSYS*|CYGWIN*) PLATFORM=windows ;;
esac
```

On Windows it publishes `src/Imrdy.Windows/Imrdy.Windows.csproj` to `~/.local/bin/imrdy.exe`, stops
the running tray (`imrdy stop` + `taskkill //IM imrdy.exe //F`), moves the old binary aside and
copies the new one, and relaunches the tray detached via `cmd //c start`.

On Linux it publishes `src/Imrdy.Linux/Imrdy.Linux.csproj` to `~/.local/bin/imrdy` (no `.exe`). The
binary swap is `install -m 0755 "$PUBLISH_BIN" "${DEST}.new.$$"` then `mv -f` over the destination —
an atomic same-directory swap chosen to avoid `ETXTBSY` if a concurrent hook process is mid-exec on
the old binary (a hazard Windows does not have, where a locked `.exe` fails to overwrite instead).

### Liveness is the flock, never the PID file

The Linux branch has a long-lived process to cycle: the publisher daemon. It decides liveness from
`~/.imrdy/daemon.lock` itself — `flock -n <lock> true` failing means a live holder — never from
`~/.imrdy/daemon.pid`, and never from `kill -0` on that pid. `kill -0` answers only "some process
this user may signal holds that pid", which is a different question; the flock is the same authority
`DaemonLock.IsRunning` uses, so both sides agree, and the PID file is only how the signal is
addressed. `flock` (util-linux) is a hard requirement: absent, the script exits 1 rather than fall
back to `kill -0`. The pid is validated against `^[1-9][0-9]*$` before it reaches `kill`, because a
leading `-` in `kill`'s target is a selector: `-1` would signal every process the invoking user owns.

The PID file is not trustworthy because only a clean stop removes it. `RunDaemon` intercepts
`SIGINT` and `SIGTERM` with a `PosixSignalRegistration` pair whose handler sets `ctx.Cancel = true`,
so a clean stop unwinds `Main` and `DaemonLock.Dispose` deletes `daemon.pid`. `SIGKILL`,
`wsl --terminate`, an unhandled crash, and signals outside that pair (`SIGHUP`, `SIGQUIT`) terminate
without unwinding: the kernel drops the flock and `daemon.pid` stays behind naming a dead pid. After
`wsl --terminate` and a distro restart the pid namespace restarts from 1 against a rootfs that kept
the file, so that pid can belong to an unrelated same-user process. Before the signal registration
existed, `SIGINT` did not stop the daemon at all (`Console.CancelKeyPress` was measured not to
dispatch on Linux) and `SIGTERM` exited at 143 without unwinding, so a stale `daemon.pid` was the
routine state after every stop.

### The stop sequence, and when it refuses to deploy

After `kill -TERM` the script polls the lock for up to 2s (ten 0.2s iterations). Release now runs
behind a real unwind — `SinkRegistry.Dispose` → `TcpSink.Dispose`, whose `_dialLoop.Wait(DisposeGrace)`
is bounded at 2s *per sink*, the poll's entire budget. The common case is milliseconds, because both
awaits in `DialLoopAsync` observe the token; the ~200ms release measured earlier was on the old
exit-143 path, where the kernel dropped the flock on process death, so re-measure before sizing
anything against it.

**When the poll runs out, the script escalates and can refuse to deploy.** If the lock is still held
after ten iterations it warns, sends `SIGKILL`, and polls ten more times; if it is *still* held it
calls `refuse_deploy`, which prints an error naming the lock file and exits 1 **before** the
`install`/`mv` swap — nothing is deployed and no relaunch line is printed. Both kills sit inside the
same `^[1-9][0-9]*$` pid guard, and every liveness test in the sequence is the same flock probe.

`refuse_deploy` has a second caller: the lock is held but `daemon.pid` does not hold a plain pid, so
the script knows a daemon is up *and* that it cannot address it. Both paths share one exit because
they enforce one property: *the script never reports success it did not achieve.* The realistic
trigger for the second is benign — `DaemonLock.Dispose` deletes the PID file *before* releasing the
lock, so a script starting inside that window reads an empty pid against a held lock — but an
unlikely trigger is an argument about reaching the branch, not about what it does once reached.

The escalation has to exist because `ctx.Cancel = true` removes the kernel's default action:
`kill -TERM` no longer guarantees the daemon dies, only that it is asked to unwind. Without the
escalation the script would fall through to the swap and print `"Daemon relaunched."` while the old
binary still held the lock; the relaunched process would hit `DaemonLock.TryAcquire` → null →
`ExitAlreadyRunning`, which is exit **0** logged to the daemon log rather than to the terminal, and
the developer would test the new binary against the old one. SIGKILL is safe to escalate to because
the kernel drops the advisory flock on process death; a second poll that still fails therefore means
a *different* holder, which is a case for a human.

### Conditional relaunch

After a successful stop the script swaps the binary and **relaunches the daemon only if one was
already running**. That asymmetry with Windows (which always respawns the tray) is deliberate: a box
with no registered links has nothing to publish, and the hook spawns the daemon on the next event
once links exist, so an unconditional launch would leave an idle process on every machine the
script has ever run on.

Both branches write the same `~/.imrdy/.dev-build` marker, containing the repo root — see
[Dev Build Marker & Logging](dev-build-marker-logging.md).

**Anything added to Core that the daemon consumes has to be checked against both branches** — a
Windows `./build-dev.sh` run proves nothing about the Linux path.
