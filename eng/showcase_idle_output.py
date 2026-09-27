"""Bounded diagnostic log tails; continue draining pipes after the budget fills."""
import threading


MAX_LOG_BYTES = 8 * 1024 * 1024


class TailCapture:
    def __init__(self, stream, output, limit):
        self.stream, self.output, self.limit = stream, output, limit
        self.buffer = bytearray(limit)
        self.position = 0
        self.retained = self.total = 0
        self.error = None
        self.thread = threading.Thread(target=self._drain, daemon=True)
        self.thread.start()

    def _drain(self):
        try:
            while chunk := self.stream.read(64 * 1024):
                self.total += len(chunk)
                if len(chunk) >= self.limit:
                    self.buffer[:] = chunk[-self.limit:]
                    self.position = 0
                else:
                    first = min(len(chunk), self.limit - self.position)
                    self.buffer[self.position:self.position + first] = chunk[:first]
                    self.buffer[:len(chunk) - first] = chunk[first:]
                    self.position = (self.position + len(chunk)) % self.limit
                self.retained = min(self.limit, self.retained + len(chunk))
        except Exception as error:
            self.error = f"{type(error).__name__}: {error}"
        finally:
            self.stream.close()

    def finish(self, timeout):
        self.thread.join(timeout)
        if self.thread.is_alive():
            # Never read a concurrently changing tail or mistake it for complete.
            return {"complete": False, "error": "Owned log pipe did not reach EOF"}
        view = memoryview(self.buffer)
        if self.retained < self.limit:
            self.output.write(view[:self.retained])
        else:
            self.output.write(view[self.position:])
            self.output.write(view[:self.position])
        self.output.flush()
        return {"complete": self.error is None, "error": self.error,
                "limitBytes": self.limit, "totalBytes": self.total,
                "retainedBytes": self.retained, "discardedPrefixBytes": self.total - self.retained}
