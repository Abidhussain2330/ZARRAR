// SPDX-License-Identifier: LicenseRef-THE-ARZENS-Custom
// Copyright (C) 2022-2025 THE ARZENS
import { Anchor, Text } from '@mantine/core'
import { FC } from 'react'

interface CopyrightProps {
  isMobile?: boolean
}

export const Copyright: FC<CopyrightProps> = ({ isMobile }) => {
  const currentYear = new Date().getFullYear()

  return (
    <Text size="sm" ta="center" fw={400} c="dimmed">
      Copyright&nbsp;©&nbsp;2022-{currentYear}&nbsp;
      {isMobile && <br />}
      <Anchor href="https://github.com/Arzen-007" c="dimmed" size="sm" fw={500}>
        @THE ARZENS
      </Anchor>
      ,&nbsp;All&nbsp;Rights&nbsp;Reserved.
    </Text>
  )
}
