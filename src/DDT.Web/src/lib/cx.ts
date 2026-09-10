export function cx(...classes: (string | false | undefined)[]): string {
  return classes.filter((value): value is string => Boolean(value)).join(" ");
}
