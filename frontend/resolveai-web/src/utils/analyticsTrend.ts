export type AnalyticsTrendPoint = {
  date: string;
  dateEnd?: string | null;
  count: number;
  granularity?: "daily" | "weekly" | "monthly";
};

export function parseLocalDate(dateStr: string): Date {
  const clean = dateStr.split("T")[0];
  const [yearStr, monthStr, dayStr] = clean.split("-");
  return new Date(
    parseInt(yearStr, 10),
    parseInt(monthStr, 10) - 1,
    parseInt(dayStr, 10)
  );
}

export function formatDateLabel(
  point: AnalyticsTrendPoint | string
): string {
  if (typeof point === "string") {
    return new Intl.DateTimeFormat("en", {
      month: "short",
      day: "numeric",
    }).format(parseLocalDate(point));
  }

  const startDate = parseLocalDate(point.date);

  if (point.granularity === "monthly") {
    return new Intl.DateTimeFormat("en", {
      month: "long",
      year: "numeric",
    }).format(startDate);
  }

  if (point.granularity === "weekly" && point.dateEnd) {
    const endDate = parseLocalDate(point.dateEnd);
    const startMonth = new Intl.DateTimeFormat("en", { month: "short" }).format(
      startDate
    );
    const endMonth = new Intl.DateTimeFormat("en", { month: "short" }).format(
      endDate
    );

    if (startDate.getMonth() === endDate.getMonth()) {
      return `${startMonth} ${startDate.getDate()}–${endDate.getDate()}`;
    }
    return `${startMonth} ${startDate.getDate()} – ${endMonth} ${endDate.getDate()}`;
  }

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
  }).format(startDate);
}

export function formatTooltipDate(
  point: AnalyticsTrendPoint | string
): string {
  if (typeof point === "string") {
    return new Intl.DateTimeFormat("en", {
      month: "short",
      day: "numeric",
      year: "numeric",
    }).format(parseLocalDate(point));
  }

  const startDate = parseLocalDate(point.date);

  if (point.granularity === "monthly") {
    return new Intl.DateTimeFormat("en", {
      month: "long",
      year: "numeric",
    }).format(startDate);
  }

  if (point.granularity === "weekly" && point.dateEnd) {
    const endDate = parseLocalDate(point.dateEnd);
    const startMonth = new Intl.DateTimeFormat("en", { month: "short" }).format(
      startDate
    );
    const endMonth = new Intl.DateTimeFormat("en", { month: "short" }).format(
      endDate
    );
    const sameYear = startDate.getFullYear() === endDate.getFullYear();

    if (sameYear) {
      return `${startMonth} ${startDate.getDate()} – ${endMonth} ${endDate.getDate()}, ${startDate.getFullYear()}`;
    }
    return `${startMonth} ${startDate.getDate()}, ${startDate.getFullYear()} – ${endMonth} ${endDate.getDate()}, ${endDate.getFullYear()}`;
  }

  return new Intl.DateTimeFormat("en", {
    month: "short",
    day: "numeric",
    year: "numeric",
  }).format(startDate);
}

export function formatAriaDate(
  point: AnalyticsTrendPoint | string
): string {
  if (typeof point === "string") {
    return new Intl.DateTimeFormat("en", {
      month: "long",
      day: "numeric",
      year: "numeric",
    }).format(parseLocalDate(point));
  }

  const startDate = parseLocalDate(point.date);

  if (point.granularity === "monthly") {
    return new Intl.DateTimeFormat("en", {
      month: "long",
      year: "numeric",
    }).format(startDate);
  }

  if (point.granularity === "weekly" && point.dateEnd) {
    const endDate = parseLocalDate(point.dateEnd);
    const startMonth = new Intl.DateTimeFormat("en", { month: "short" }).format(
      startDate
    );
    const endMonth = new Intl.DateTimeFormat("en", { month: "short" }).format(
      endDate
    );
    return `${startMonth} ${startDate.getDate()} – ${endMonth} ${endDate.getDate()}, ${startDate.getFullYear()}`;
  }

  return new Intl.DateTimeFormat("en", {
    month: "long",
    day: "numeric",
    year: "numeric",
  }).format(startDate);
}

export function reconcileTrendTotal(trend: AnalyticsTrendPoint[]): number {
  return trend.reduce((sum, item) => sum + item.count, 0);
}
