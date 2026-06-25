export function ok<T>(data: T, message = "OK") {
  return {
    success: true,
    message,
    data,
    errors: [] as string[],
  };
}

export function fail(message: string, errors: string[] = [message]) {
  return {
    success: false,
    message,
    data: null,
    errors,
  };
}

